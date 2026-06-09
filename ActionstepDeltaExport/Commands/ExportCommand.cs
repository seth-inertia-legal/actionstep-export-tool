using System.Globalization;
using ActionstepDeltaExport.Models;
using ActionstepDeltaExport.Services;

namespace ActionstepDeltaExport.Commands;

/// <summary>
/// Core delta-export command.
///
/// Workflow:
///   1. Authenticate (browser flow on first run, token cache thereafter).
///   2. Load the existing manifest to build a lookup index.
///   3. Stream actiondocuments modified since <c>SinceDate</c> from the API.
///   4. For each document:
///      - Live run : download the file into OutputRoot/{action_id}/
///      - Dry run  : record what would be downloaded, skip file transfer
///   5. Upsert the record into the merged manifest.
///   6. Write a new dated manifest CSV next to the original.
///   7. Append any per-document errors to errors_{timestamp}.csv.
/// </summary>
public static class ExportCommand
{
    private const long MinDownloadableSize = 1;   // Skip truly empty files.

    public static async Task<int> RunAsync(
        AppSettings settings,
        bool dryRun = false,
        CancellationToken ct = default)
    {
        string runTimestamp = DateTime.UtcNow.ToString("yyyyMMdd_HHmmss");

        if (dryRun)
        {
            Console.WriteLine("*** DRY RUN — no files will be downloaded ***");
            Console.WriteLine();
        }

        // ── 1. Resolve configuration ──────────────────────────────────────────

        string manifestPath = settings.Export.ManifestPath;
        string outputRoot   = settings.Export.OutputRoot;

        if (!DateTime.TryParse(settings.Export.SinceDate, CultureInfo.InvariantCulture,
                DateTimeStyles.AssumeUniversal | DateTimeStyles.AdjustToUniversal,
                out DateTime sinceDate))
        {
            Console.Error.WriteLine(
                $"Invalid SinceDate in configuration: \"{settings.Export.SinceDate}\".");
            return 1;
        }

        Console.WriteLine($"Delta export starting.");
        Console.WriteLine($"  Manifest : {manifestPath}");
        Console.WriteLine($"  Output   : {outputRoot}");
        Console.WriteLine($"  Since    : {sinceDate:yyyy-MM-dd HH:mm:ss} UTC");
        Console.WriteLine();

        // ── 2. Load existing manifest ─────────────────────────────────────────

        var existingRecords = ManifestService.Load(manifestPath);
        Console.WriteLine($"  Loaded {existingRecords.Count:N0} existing manifest record(s).");

        // Index by LogId (document ID as string key) for fast upsert.
        var manifestIndex = existingRecords
            .ToDictionary(r => r.LogId.ToString(), r => r);

        // ── 3. Authenticate ───────────────────────────────────────────────────

        Console.WriteLine("Authenticating...");
        using var auth = new AuthService(settings.Actionstep);
        var token = await auth.GetValidTokenAsync(ct);
        Console.WriteLine($"Authenticated. Org: {token.OrgKey}");
        Console.WriteLine();

        using var apiClient = new ActionstepApiClient(token);

        // ── 4. Stream and (optionally) download documents ─────────────────────

        var errors    = new List<ExportError>();
        int downloaded = 0;
        int wouldDownload = 0;
        long totalBytes   = 0;
        int skipped    = 0;
        int deleted    = 0;

        // Lazy per-action folder cache: populated on first document seen per action.
        // Key = action_id string; Value = folderId → full path dictionary.
        var folderCache = new Dictionary<string, Dictionary<int, string>>(StringComparer.Ordinal);

        await foreach (var doc in apiClient.GetDocumentsSinceAsync(sinceDate, ct))
        {
            // a) Handle soft-deleted documents: update manifest only, no file.
            if (doc.IsDeleted)
            {
                string? deletedFolderPath = await ResolveFolderPathAsync(doc, folderCache, apiClient, ct);
                UpsertRecord(manifestIndex, doc, outputPath: null, deletedFolderPath);
                deleted++;
                continue;
            }

            // b) Skip placeholder documents (no file content).
            if (string.IsNullOrWhiteSpace(doc.FileIdentifier) ||
                string.IsNullOrWhiteSpace(doc.FileName)       ||
                (doc.FileSize ?? 0) < MinDownloadableSize)
            {
                skipped++;
                continue;
            }

            // c) Build output path: OutputRoot/{action_id}/{fileName}
            // (FileName is guaranteed non-null by the guard above.)
            string actionId   = doc.Links?.Action ?? "unknown";
            string safeFile   = SanitizeFileName(doc.FileName!);
            string outputPath = Path.Combine(outputRoot, actionId, safeFile);

            string? folderPath = await ResolveFolderPathAsync(doc, folderCache, apiClient, ct);

            if (dryRun)
            {
                // d-dry) Record intent without transferring.
                Console.WriteLine($"  [DRY RUN] {actionId}/{safeFile} ({doc.FileSize ?? 0:N0} bytes)");
                wouldDownload++;
                totalBytes += doc.FileSize ?? 0;
                UpsertRecord(manifestIndex, doc, outputPath, folderPath);
            }
            else
            {
                // d-live) Download with skip-and-continue error handling.
                try
                {
                    Console.Write($"  Downloading doc {doc.Id} ({doc.FileSize ?? 0:N0} bytes) " +
                                  $"→ {actionId}/{safeFile} ... ");

                    await apiClient.DownloadFileAsync(doc.FileIdentifier, doc.FileSize ?? 0, outputPath, ct);

                    Console.WriteLine("OK");
                    downloaded++;
                    UpsertRecord(manifestIndex, doc, outputPath, folderPath);
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

        // ── 5. Write merged manifest ──────────────────────────────────────────

        string manifestBase = Path.GetFileNameWithoutExtension(manifestPath);
        string suffix       = dryRun ? $"_dryrun_{runTimestamp}" : $"_{runTimestamp}";
        string newManifest  = Path.Combine(outputRoot, $"{manifestBase}{suffix}.csv");

        var allRecords = manifestIndex.Values.OrderBy(r => r.ActionId).ThenBy(r => r.LogId);
        ManifestService.Write(newManifest, allRecords);
        Console.WriteLine();
        Console.WriteLine($"  Manifest written: {newManifest}");

        // ── 6. Write errors CSV if any (live run only) ────────────────────────

        if (!dryRun && errors.Count > 0)
        {
            string errorCsv = Path.Combine(outputRoot, $"errors_{runTimestamp}.csv");
            WriteErrorsCsv(errorCsv, errors);
            Console.WriteLine($"  Errors CSV      : {errorCsv}  ({errors.Count} error(s))");
        }

        // ── 7. Summary ────────────────────────────────────────────────────────

        Console.WriteLine();
        if (dryRun)
        {
            Console.WriteLine($"Dry run complete:");
            Console.WriteLine($"  Would download : {wouldDownload:N0} file(s) " +
                               $"({totalBytes / 1024.0 / 1024.0:N1} MB total)");
            Console.WriteLine($"  Deleted        : {deleted:N0}  (manifest-only)");
            Console.WriteLine($"  Skipped        : {skipped:N0}  (placeholder/empty)");
        }
        else
        {
            Console.WriteLine($"Export complete:");
            Console.WriteLine($"  Downloaded : {downloaded:N0}");
            Console.WriteLine($"  Deleted    : {deleted:N0}  (manifest-only update)");
            Console.WriteLine($"  Skipped    : {skipped:N0}  (placeholder/empty)");
            Console.WriteLine($"  Errors     : {errors.Count:N0}");
        }

        return errors.Count > 0 ? 2 : 0;   // Exit 2 = partial success (live run only).
    }

    // ── Helpers ───────────────────────────────────────────────────────────────

    /// <summary>Adds or replaces a record in <paramref name="index"/>.</summary>
    private static void UpsertRecord(
        Dictionary<string, ManifestRecord> index,
        ActionDocument doc,
        string? outputPath,
        string? folderPath = null)
    {
        var record = new ManifestRecord
        {
            LogId             = doc.Id,
            ActionId          = int.TryParse(doc.Links?.Action, out int aid) ? aid : 0,
            DocumentName      = doc.Name,
            TemplateId        = null,
            FileName          = doc.FileName,
            Directory         = outputPath is not null ? Path.GetDirectoryName(outputPath) : null,
            FolderId          = doc.Links?.Folder,
            FileType          = doc.FileName is not null
                                    ? Path.GetExtension(doc.FileName).TrimStart('.').ToUpperInvariant()
                                    : null,
            CreatedBy         = null,
            ModifiedBy        = null,
            CreatedDate       = doc.CreatedTimestamp?.UtcDateTime,
            LastModified      = doc.ModifiedTimestamp?.UtcDateTime,
            DocumentTimestamp = doc.ModifiedTimestamp?.UtcDateTime,
            IsDeleted         = doc.IsDeleted,
            FolderPath        = folderPath
        };

        index[doc.Id.ToString()] = record;
    }

    /// <summary>
    /// Resolves the full folder path for a document using a per-action lazy cache.
    /// Returns null if the document has no folder, or if folder data is unavailable.
    /// </summary>
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

    /// <summary>Strips characters that are invalid in Windows/macOS file names.</summary>
    private static string SanitizeFileName(string name)
    {
        var invalid = Path.GetInvalidFileNameChars();
        foreach (char c in invalid)
            name = name.Replace(c, '_');
        return name;
    }

    private static void WriteErrorsCsv(string path, IEnumerable<ExportError> errors)
    {
        using var writer = new System.IO.StreamWriter(path, append: false);
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
        public string  DocumentId   { get; set; } = "";
        public string  ActionId     { get; set; } = "";
        public string? FileName     { get; set; }
        public string? ErrorMessage { get; set; }
        public DateTime Timestamp   { get; set; }
    }
}
