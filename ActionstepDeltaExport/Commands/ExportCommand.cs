using System.Diagnostics;
using System.Globalization;
using CsvHelper;
using CsvHelper.Configuration;
using ActionstepDeltaExport.Models;
using ActionstepDeltaExport.Services;

namespace ActionstepDeltaExport.Commands;

/// <summary>
/// Core export command.
///
/// include = subset of { "documents", "actions", "folders" }
/// download = true  → download document files  (documents only)
///          = false → write audit/manifest CSV without downloading
///
/// Actions and folders always write a full-dump CSV regardless of --download.
/// --since (SinceDate) is only applied when "documents" is in include.
/// </summary>
public static class ExportCommand
{
    private const long MinDownloadableSize = 1;

    public static async Task<int> RunAsync(
        AppSettings settings,
        HashSet<string> include,
        bool download = false,
        CancellationToken ct = default)
    {
        string runTimestamp = DateTime.UtcNow.ToString("yyyyMMdd_HHmmss");
        string outputRoot   = settings.Export.OutputRoot;

        // ── Parse sinceDate (documents only) ─────────────────────────────────

        DateTime sinceDate = default;
        if (include.Contains("documents"))
        {
            if (!DateTime.TryParse(settings.Export.SinceDate, CultureInfo.InvariantCulture,
                    DateTimeStyles.AssumeUniversal | DateTimeStyles.AdjustToUniversal,
                    out sinceDate))
            {
                Console.Error.WriteLine(
                    $"Invalid SinceDate in configuration: \"{settings.Export.SinceDate}\".");
                return 1;
            }
        }

        // ── Print run header ─────────────────────────────────────────────────

        Console.WriteLine("Export starting.");
        Console.WriteLine($"  Include  : {string.Join(", ", include)}");
        Console.WriteLine($"  Output   : {outputRoot}");
        if (include.Contains("documents"))
        {
            Console.WriteLine($"  Since    : {sinceDate:yyyy-MM-dd HH:mm:ss} UTC");
            Console.WriteLine($"  Download : {(download ? "yes" : "no (audit only)")}");
        }
        Console.WriteLine();

        // ── Authenticate (once, shared across all sub-exports) ────────────────

        Console.WriteLine("Authenticating...");
        using var auth = new AuthService(settings.Actionstep);
        var token = await auth.GetValidTokenAsync(ct);
        Console.WriteLine($"Authenticated. Org: {token.OrgKey}");
        Console.WriteLine();

        using var apiClient = new ActionstepApiClient(token);

        int exitCode = 0;

        if (include.Contains("documents"))
        {
            int result = await ExportDocumentsAsync(
                settings, apiClient, sinceDate, download, outputRoot, runTimestamp, ct);
            if (result != 0) exitCode = result;
        }

        if (include.Contains("actions"))
            await ExportActionsAsync(apiClient, outputRoot, runTimestamp, ct);

        if (include.Contains("folders"))
            await ExportFoldersAsync(apiClient, outputRoot, runTimestamp, ct);

        return exitCode;
    }

    // ── Documents ─────────────────────────────────────────────────────────────

    private static async Task<int> ExportDocumentsAsync(
        AppSettings settings,
        ActionstepApiClient apiClient,
        DateTime sinceDate,
        bool download,
        string outputRoot,
        string runTimestamp,
        CancellationToken ct)
    {
        string manifestPath = settings.Export.ManifestPath;

        if (!download)
        {
            Console.WriteLine("*** AUDIT MODE — no files will be downloaded ***");
            Console.WriteLine();
        }

        // Load existing manifest
        var existingRecords = ManifestService.Load(manifestPath);
        Console.WriteLine($"  Loaded {existingRecords.Count:N0} existing manifest record(s).");
        var manifestIndex = existingRecords.ToDictionary(r => r.LogId.ToString(), r => r);

        var errors      = new List<ExportError>();
        int downloaded  = 0;
        int audited     = 0;
        long totalBytes = 0;
        int skipped     = 0;
        int deleted     = 0;
        int processed   = 0;

        var sw          = Stopwatch.StartNew();
        var folderCache = new Dictionary<string, Dictionary<int, string>>(StringComparer.Ordinal);

        await foreach (var doc in apiClient.GetDocumentsSinceAsync(sinceDate, ct))
        {
            processed++;

            // Soft-deleted: update manifest only, no file transfer
            if (doc.IsDeleted)
            {
                string? delPath      = await ResolveFolderPathAsync(doc, folderCache, apiClient, ct);
                string? delCreatedBy = await apiClient.GetParticipantNameAsync(doc.Links?.CreatedBy, ct);
                UpsertRecord(manifestIndex, doc, outputPath: null, delPath, delCreatedBy);
                deleted++;
                continue;
            }

            // Skip placeholders (no file content)
            if (string.IsNullOrWhiteSpace(doc.FileIdentifier) ||
                string.IsNullOrWhiteSpace(doc.FileName)       ||
                (doc.FileSize ?? 0) < MinDownloadableSize)
            {
                skipped++;
                continue;
            }

            string actionId   = doc.Links?.Action ?? "unknown";
            string safeFile   = SanitizeFileName(doc.FileName!);
            string outputPath = Path.Combine(outputRoot, actionId, safeFile);

            string? folderPath = await ResolveFolderPathAsync(doc, folderCache, apiClient, ct);
            string? createdBy  = await apiClient.GetParticipantNameAsync(doc.Links?.CreatedBy, ct);

            if (!download)
            {
                // Audit: record intent without transferring
                audited++;
                totalBytes += doc.FileSize ?? 0;
                UpsertRecord(manifestIndex, doc, outputPath, folderPath, createdBy);

                if (audited % 100 == 0)
                {
                    int    total  = apiClient.TotalDocumentCount;
                    double rate   = sw.Elapsed.TotalSeconds > 0 ? processed / sw.Elapsed.TotalSeconds : 0;
                    string etaStr = (rate > 0 && total > processed)
                        ? FormatDuration(TimeSpan.FromSeconds((total - processed) / rate))
                        : "—";
                    Console.WriteLine(
                        $"  [AUDIT] {audited:N0}/{total:N0} docs" +
                        $"  |  {folderCache.Count} action(s) resolved" +
                        $"  |  elapsed {FormatDuration(sw.Elapsed)}" +
                        $"  |  ETA {etaStr}");
                }
            }
            else
            {
                // Download mode
                try
                {
                    Console.Write(
                        $"  [{FormatDuration(sw.Elapsed)}] Downloading doc {doc.Id} " +
                        $"({doc.FileSize ?? 0:N0} bytes) → {actionId}/{safeFile} ... ");

                    await apiClient.DownloadFileAsync(doc.FileIdentifier, doc.FileSize ?? 0, outputPath, ct);

                    Console.WriteLine("OK");
                    downloaded++;
                    UpsertRecord(manifestIndex, doc, outputPath, folderPath, createdBy);
                }
                catch (Exception ex) when (ex is not OperationCanceledException)
                {
                    Console.WriteLine($"FAILED: {ex.Message}");
                    errors.Add(new ExportError
                    {
                        DocumentId   = doc.Id.ToString(),
                        ActionId     = actionId,
                        FileName     = doc.FileName,
                        ErrorMessage = ex.Message,
                        Timestamp    = DateTime.UtcNow
                    });
                }
            }
        }

        Console.WriteLine(
            $"  Streaming complete: {processed:N0} total doc(s) seen in {FormatDuration(sw.Elapsed)}.");

        // ── Write documents CSV ───────────────────────────────────────────────

        string csvName    = download
            ? $"documents_{runTimestamp}.csv"
            : $"documents_audit_{runTimestamp}.csv";
        string csvPath    = Path.Combine(outputRoot, csvName);
        var    allRecords = manifestIndex.Values.OrderBy(r => r.ActionId).ThenBy(r => r.LogId);
        ManifestService.Write(csvPath, allRecords);
        Console.WriteLine();
        Console.WriteLine($"  Documents CSV : {csvPath}");

        // ── Write errors CSV (download mode only) ─────────────────────────────

        if (download && errors.Count > 0)
        {
            string errorCsv = Path.Combine(outputRoot, $"errors_{runTimestamp}.csv");
            WriteErrorsCsv(errorCsv, errors);
            Console.WriteLine($"  Errors CSV    : {errorCsv}  ({errors.Count} error(s))");
        }

        // ── Summary ───────────────────────────────────────────────────────────

        Console.WriteLine();
        if (!download)
        {
            Console.WriteLine("Documents audit complete:");
            Console.WriteLine(
                $"  Audited  : {audited:N0} file(s) ({totalBytes / 1024.0 / 1024.0:N1} MB total)");
            Console.WriteLine($"  Deleted  : {deleted:N0}  (manifest-only)");
            Console.WriteLine($"  Skipped  : {skipped:N0}  (placeholder/empty)");
            Console.WriteLine($"  Elapsed  : {FormatDuration(sw.Elapsed)}");
        }
        else
        {
            Console.WriteLine("Documents export complete:");
            Console.WriteLine($"  Downloaded : {downloaded:N0}");
            Console.WriteLine($"  Deleted    : {deleted:N0}  (manifest-only update)");
            Console.WriteLine($"  Skipped    : {skipped:N0}  (placeholder/empty)");
            Console.WriteLine($"  Errors     : {errors.Count:N0}");
            Console.WriteLine($"  Elapsed    : {FormatDuration(sw.Elapsed)}");
        }
        Console.WriteLine();

        return errors.Count > 0 ? 2 : 0;
    }

    // ── Actions ───────────────────────────────────────────────────────────────

    private static async Task ExportActionsAsync(
        ActionstepApiClient apiClient,
        string outputRoot,
        string runTimestamp,
        CancellationToken ct)
    {
        Console.WriteLine("Exporting actions (full dump)...");
        var sw      = Stopwatch.StartNew();
        var records = new List<ActionRecord>();

        await foreach (var action in apiClient.GetAllActionsAsync(ct))
        {
            string? typeId   = action.Links?.ActionType;
            string? typeName = await apiClient.GetActionTypeNameAsync(typeId, ct);

            records.Add(new ActionRecord
            {
                ActionId       = action.Id,
                ActionName     = action.Name,
                ActionTypeId   = typeId,
                FileReference  = action.Reference,
                ActionTypeName = typeName
            });
        }

        string csvPath = Path.Combine(outputRoot, $"actions_{runTimestamp}.csv");
        WriteCsv<ActionRecord, ActionRecordMap>(csvPath, records);

        Console.WriteLine(
            $"  Actions CSV : {csvPath}  ({records.Count:N0} record(s)) in {FormatDuration(sw.Elapsed)}");
        Console.WriteLine();
    }

    // ── Folders ───────────────────────────────────────────────────────────────

    private static async Task ExportFoldersAsync(
        ActionstepApiClient apiClient,
        string outputRoot,
        string runTimestamp,
        CancellationToken ct)
    {
        Console.WriteLine("Exporting folders (full dump)...");
        var sw         = Stopwatch.StartNew();
        var allFolders = new List<ActionFolder>();

        await foreach (var folder in apiClient.GetAllFoldersAsync(ct))
            allFolders.Add(folder);

        // Build global path dictionary (id → full path string)
        var pathDict = FolderPathResolver.BuildPathDictionary(allFolders);

        var records = allFolders.Select(f => new FolderRecord
        {
            FolderId       = f.Id,
            ActionId       = f.Links?.Action,
            Name           = f.Name,
            ParentFolderId = f.Links?.ParentFolder,
            FolderPath     = pathDict.TryGetValue(f.Id, out string? p) ? p : null
        }).ToList();

        string csvPath = Path.Combine(outputRoot, $"folders_{runTimestamp}.csv");
        WriteCsv<FolderRecord, FolderRecordMap>(csvPath, records);

        Console.WriteLine(
            $"  Folders CSV : {csvPath}  ({records.Count:N0} record(s)) in {FormatDuration(sw.Elapsed)}");
        Console.WriteLine();
    }

    // ── CSV writer ────────────────────────────────────────────────────────────

    private static void WriteCsv<TRecord, TMap>(string path, IEnumerable<TRecord> records)
        where TMap : ClassMap<TRecord>
    {
        string? dir = Path.GetDirectoryName(path);
        if (!string.IsNullOrEmpty(dir))
            Directory.CreateDirectory(dir);

        using var writer = new StreamWriter(path, append: false, System.Text.Encoding.UTF8);
        using var csv    = new CsvWriter(writer,
            new CsvConfiguration(CultureInfo.InvariantCulture));
        csv.Context.RegisterClassMap<TMap>();
        csv.WriteRecords(records);
    }

    // ── Shared helpers ────────────────────────────────────────────────────────

    private static void UpsertRecord(
        Dictionary<string, ManifestRecord> index,
        ActionDocument doc,
        string? outputPath,
        string? folderPath = null,
        string? createdBy  = null)
    {
        index.TryGetValue(doc.Id.ToString(), out ManifestRecord? existing);

        var record = new ManifestRecord
        {
            LogId             = doc.Id,
            ActionId          = int.TryParse(doc.Links?.Action, out int aid) ? aid : 0,
            DocumentName      = doc.Name,
            TemplateId        = existing?.TemplateId,
            FileName          = doc.FileName,
            Directory         = outputPath is not null ? Path.GetDirectoryName(outputPath) : null,
            FolderId          = doc.Links?.Folder,
            FileType          = doc.FileName is not null
                                    ? Path.GetExtension(doc.FileName).TrimStart('.').ToUpperInvariant()
                                    : null,
            CreatedBy         = createdBy ?? existing?.CreatedBy,
            ModifiedBy        = doc.CheckedOutTo ?? createdBy ?? existing?.ModifiedBy,
            CreatedDate       = doc.CreatedTimestamp?.UtcDateTime,
            LastModified      = doc.ModifiedTimestamp?.UtcDateTime,
            DocumentTimestamp = doc.ModifiedTimestamp?.UtcDateTime,
            IsDeleted         = doc.IsDeleted,
            FolderPath        = folderPath ?? existing?.FolderPath
        };

        index[doc.Id.ToString()] = record;
    }

    private static async Task<string?> ResolveFolderPathAsync(
        ActionDocument doc,
        Dictionary<string, Dictionary<int, string>> folderCache,
        ActionstepApiClient apiClient,
        CancellationToken ct)
    {
        string? folderIdStr = doc.Links?.Folder;
        if (string.IsNullOrEmpty(folderIdStr) || !int.TryParse(folderIdStr, out int folderId))
            return null;

        string actionId = doc.Links?.Action ?? "unknown";

        if (!folderCache.TryGetValue(actionId, out var pathDict))
        {
            var folders = await apiClient.GetFoldersForActionAsync(actionId, ct);
            pathDict = FolderPathResolver.BuildPathDictionary(folders);
            folderCache[actionId] = pathDict;
        }

        return pathDict.TryGetValue(folderId, out string? path) ? path : null;
    }

    private static string FormatDuration(TimeSpan t)
    {
        if (t.TotalHours >= 1)
            return $"{(int)t.TotalHours}h {t.Minutes:D2}m {t.Seconds:D2}s";
        if (t.TotalMinutes >= 1)
            return $"{(int)t.TotalMinutes}m {t.Seconds:D2}s";
        return $"{t.Seconds}s";
    }

    private static string SanitizeFileName(string name)
    {
        var invalid = Path.GetInvalidFileNameChars();
        foreach (char c in invalid)
            name = name.Replace(c, '_');
        return name;
    }

    private static void WriteErrorsCsv(string path, IEnumerable<ExportError> errors)
    {
        using var writer = new StreamWriter(path, append: false);
        writer.WriteLine("document_id,action_id,file_name,error_message,timestamp");
        foreach (var e in errors)
        {
            writer.WriteLine(
                $"\"{e.DocumentId}\"," +
                $"\"{e.ActionId}\"," +
                $"\"{e.FileName?.Replace("\"", "\"\"")}\"," +
                $"\"{e.ErrorMessage?.Replace("\"", "\"\"")}\"," +
                $"\"{e.Timestamp:yyyy-MM-dd HH:mm:ss}\"");
        }
    }

    private sealed class ExportError
    {
        public string   DocumentId   { get; set; } = "";
        public string   ActionId     { get; set; } = "";
        public string?  FileName     { get; set; }
        public string?  ErrorMessage { get; set; }
        public DateTime Timestamp    { get; set; }
    }
}
