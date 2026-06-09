using System.Net.Http.Headers;
using System.Runtime.CompilerServices;
using System.Text.Json;
using ActionstepDeltaExport.Models;

namespace ActionstepDeltaExport.Services;

/// <summary>
/// Thin HTTP client for the Actionstep REST API.
/// Handles paginated document listing and chunked file downloads.
/// </summary>
public sealed class ActionstepApiClient : IDisposable
{
    private const int ChunkSize = 5 * 1024 * 1024;   // 5 MB

    private static readonly JsonSerializerOptions JsonOpts = new()
    {
        PropertyNameCaseInsensitive = true
    };

    private readonly HttpClient _http;

    // api_endpoint already contains the path up to "/api/", e.g.:
    //   "https://ap-southeast-2.actionstep.com/api/"
    // All resource paths are relative to this, e.g. "rest/actiondocuments"
    private readonly string _base;

    /// <summary>
    /// Total document count returned by the first page of the most recent
    /// <see cref="GetDocumentsSinceAsync"/> call.  Zero until the first page
    /// has been received.  Used by callers to compute ETA.
    /// </summary>
    public int TotalDocumentCount { get; private set; }

    public ActionstepApiClient(TokenData token)
    {
        _base = token.ApiEndpoint.TrimEnd('/') + "/";

        _http = new HttpClient
        {
            BaseAddress = new Uri(_base),
            Timeout     = TimeSpan.FromMinutes(10)   // Large queries can take > 100 s default.
        };
        _http.DefaultRequestHeaders.Authorization =
            new AuthenticationHeaderValue("Bearer", token.AccessToken);
        _http.DefaultRequestHeaders.Accept.ParseAdd("application/vnd.api+json");
    }

    // ── Document listing ──────────────────────────────────────────────────────

    /// <summary>
    /// Streams all actiondocuments whose modifiedTimestamp is greater than or
    /// equal to <paramref name="since"/> (UTC).  Pages automatically using
    /// pageSize=200 until the API reports no further pages.
    /// </summary>
    public async IAsyncEnumerable<ActionDocument> GetDocumentsSinceAsync(
        DateTime since,
        [EnumeratorCancellation] CancellationToken ct = default)
    {
        // Actionstep datetime filter format: ISO-8601 without timezone offset.
        string sinceParam = since.ToUniversalTime().ToString("yyyy-MM-ddTHH:mm:ss");

        int page       = 1;
        int totalPages = int.MaxValue;   // Updated after the first response.

        while (page <= totalPages)
        {
            Console.WriteLine(page == 1
                ? "  → Fetching page 1 (may take a while — server is counting all matching records)..."
                : $"  → Fetching page {page}/{totalPages} ...");

            string url =
                $"rest/actiondocuments" +
                $"?modifiedTimestamp_gteq={Uri.EscapeDataString(sinceParam)}" +
                $"&sort=-modifiedTimestamp" +
                $"&pageSize=200" +
                $"&page={page}";

            var response = await _http.GetAsync(url, ct);

            if (!response.IsSuccessStatusCode)
            {
                string errorBody = await response.Content.ReadAsStringAsync(ct);
                throw new HttpRequestException(
                    $"GET {url} → {(int)response.StatusCode} {response.ReasonPhrase}: {errorBody}");
            }

            string body = await response.Content.ReadAsStringAsync(ct);
            var result  = JsonSerializer.Deserialize<ActionDocumentListResponse>(body, JsonOpts);

            if (result is null || result.Documents.Count == 0)
                yield break;

            foreach (var doc in result.Documents)
                yield return doc;

            // Determine whether there are more pages.
            // Paging is nested: meta.paging.actiondocuments.{recordCount, pageCount, ...}
            if (result.Meta?.Paging?.ActionDocuments is { } paging)
            {
                totalPages = paging.PageCount > 0 ? paging.PageCount : 1;

                if (page == 1)
                {
                    TotalDocumentCount = paging.TotalCount;
                    Console.WriteLine(
                        $"  → {paging.TotalCount:N0} document(s) in delta across " +
                        $"{totalPages} page(s).");
                }
            }
            else
            {
                // API returned no paging metadata — treat this as the only page.
                yield break;
            }

            page++;
        }
    }

    // ── File download ─────────────────────────────────────────────────────────

    /// <summary>
    /// Downloads <paramref name="fileIdentifier"/> in 5 MB chunks and writes
    /// the assembled file to <paramref name="outputPath"/>.
    /// Parent directories are created automatically.
    /// </summary>
    public async Task DownloadFileAsync(
        string fileIdentifier,
        long   fileSize,
        string outputPath,
        CancellationToken ct = default)
    {
        int totalChunks = (int)(fileSize / ChunkSize);
        if (fileSize % ChunkSize != 0) totalChunks++;
        if (totalChunks == 0) totalChunks = 1;

        string? dir = Path.GetDirectoryName(outputPath);
        if (!string.IsNullOrEmpty(dir))
            Directory.CreateDirectory(dir);

        await using var fs = new FileStream(
            outputPath, FileMode.Create, FileAccess.Write, FileShare.None,
            bufferSize: 81920, useAsync: true);

        for (int i = 0; i < totalChunks; i++)
        {
            // NOTE: fileIdentifier may contain "::" and ";" which are valid in
            // URL path segments and must NOT be percent-encoded here — the
            // Actionstep server expects them verbatim (same as the Postman example).
            string url = $"rest/files/{fileIdentifier}?part_number={i + 1}";

            var response = await _http.GetAsync(url, HttpCompletionOption.ResponseHeadersRead, ct);

            if (!response.IsSuccessStatusCode)
                throw new HttpRequestException(
                    $"Chunk {i + 1}/{totalChunks} failed: " +
                    $"{(int)response.StatusCode} {response.ReasonPhrase}");

            await using var chunkStream = await response.Content.ReadAsStreamAsync(ct);
            await chunkStream.CopyToAsync(fs, ct);
        }
    }

    // ── Actions listing ──────────────────────────────────────────────────────

    /// <summary>
    /// Streams all actions (matters) in the org.  No date filter — always a full dump.
    /// </summary>
    public async IAsyncEnumerable<ActionstepAction> GetAllActionsAsync(
        [EnumeratorCancellation] CancellationToken ct = default)
    {
        int page       = 1;
        int totalPages = int.MaxValue;

        while (page <= totalPages)
        {
            Console.WriteLine(page == 1
                ? "  → Fetching actions page 1..."
                : $"  → Fetching actions page {page}/{totalPages} ...");

            string url = $"rest/actions?pageSize=200&page={page}";

            var response = await _http.GetAsync(url, ct);
            if (!response.IsSuccessStatusCode)
            {
                string err = await response.Content.ReadAsStringAsync(ct);
                throw new HttpRequestException(
                    $"GET {url} → {(int)response.StatusCode} {response.ReasonPhrase}: {err}");
            }

            string body   = await response.Content.ReadAsStringAsync(ct);
            var    result = JsonSerializer.Deserialize<ActionstepActionListResponse>(body, JsonOpts);

            if (result is null || result.Actions.Count == 0)
                yield break;

            foreach (var action in result.Actions)
                yield return action;

            if (result.Meta?.Paging?.Actions is { } paging)
            {
                totalPages = paging.PageCount > 0 ? paging.PageCount : 1;
                if (page == 1)
                    Console.WriteLine($"  → {paging.TotalCount:N0} action(s) across {totalPages} page(s).");
            }
            else
            {
                yield break;
            }

            page++;
        }
    }

    // ── Action type lookup ────────────────────────────────────────────────────

    private readonly Dictionary<string, string?> _actionTypeCache =
        new(StringComparer.Ordinal);

    /// <summary>
    /// Returns the display name for an action type by ID.
    /// Results are cached for the lifetime of this client instance.
    /// </summary>
    public async Task<string?> GetActionTypeNameAsync(
        string? typeId,
        CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(typeId)) return null;

        if (_actionTypeCache.TryGetValue(typeId, out string? cached))
            return cached;

        string url = $"rest/actiontypes/{Uri.EscapeDataString(typeId)}";

        try
        {
            var response = await _http.GetAsync(url, ct);
            if (!response.IsSuccessStatusCode)
            {
                _actionTypeCache[typeId] = null;
                return null;
            }

            string body   = await response.Content.ReadAsStringAsync(ct);
            var    result = JsonSerializer.Deserialize<ActionTypeResponse>(body, JsonOpts);
            string? name  = result?.ActionType?.Name;
            _actionTypeCache[typeId] = name;
            return name;
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            Console.WriteLine($"  [WARN] Failed to fetch action type {typeId}: {ex.Message}");
            _actionTypeCache[typeId] = null;
            return null;
        }
    }

    // ── Global folder listing ─────────────────────────────────────────────────

    /// <summary>
    /// Streams ALL actionfolders in the org without an action filter.
    /// Used for the --include folders full dump.
    /// </summary>
    public async IAsyncEnumerable<ActionFolder> GetAllFoldersAsync(
        [EnumeratorCancellation] CancellationToken ct = default)
    {
        int page       = 1;
        int totalPages = int.MaxValue;

        while (page <= totalPages)
        {
            Console.WriteLine(page == 1
                ? "  → Fetching folders page 1..."
                : $"  → Fetching folders page {page}/{totalPages} ...");

            string url = $"rest/actionfolders?pageSize=200&page={page}";

            HttpResponseMessage response;
            string body;

            try
            {
                response = await _http.GetAsync(url, ct);
                body     = await response.Content.ReadAsStringAsync(ct);
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                Console.WriteLine($"  [WARN] Failed to fetch folders page {page}: {ex.Message}");
                yield break;
            }

            if (!response.IsSuccessStatusCode)
            {
                Console.WriteLine(
                    $"  [WARN] GET {url} → {(int)response.StatusCode}: aborting folder dump.");
                yield break;
            }

            ActionFolderListResponse? result;
            try
            {
                result = JsonSerializer.Deserialize<ActionFolderListResponse>(body, JsonOpts);
            }
            catch (JsonException ex)
            {
                Console.WriteLine($"  [WARN] Could not parse folder response page {page}: {ex.Message}");
                yield break;
            }

            if (result is null || result.Folders.Count == 0)
                yield break;

            foreach (var folder in result.Folders)
                yield return folder;

            if (result.Meta?.Paging?.ActionFolders is { } paging)
            {
                totalPages = paging.PageCount > 0 ? paging.PageCount : 1;
                if (page == 1)
                    Console.WriteLine($"  → {paging.TotalCount:N0} folder(s) across {totalPages} page(s).");
            }
            else
            {
                yield break;
            }

            page++;
        }
    }

    // ── Folder listing (per-action) ───────────────────────────────────────────

    /// <summary>
    /// Fetches all actionfolders belonging to <paramref name="actionId"/>.
    /// Returns an empty list (never throws) if the request fails, so a missing
    /// folder tree never aborts an export run.
    /// </summary>
    public async Task<List<ActionFolder>> GetFoldersForActionAsync(
        string actionId,
        CancellationToken ct = default)
    {
        var folders    = new List<ActionFolder>();
        int page       = 1;
        int totalPages = int.MaxValue;

        while (page <= totalPages)
        {
            string url =
                $"rest/actionfolders" +
                $"?action_eq={Uri.EscapeDataString(actionId)}" +
                $"&pageSize=200" +
                $"&page={page}";

            HttpResponseMessage response;
            string body;

            try
            {
                response = await _http.GetAsync(url, ct);
                body     = await response.Content.ReadAsStringAsync(ct);
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                Console.WriteLine($"  [WARN] Failed to fetch folders for action {actionId}: {ex.Message}");
                return folders;
            }

            if (!response.IsSuccessStatusCode)
            {
                Console.WriteLine(
                    $"  [WARN] GET {url} → {(int)response.StatusCode}: folder paths will be empty for action {actionId}.");
                return folders;
            }

            ActionFolderListResponse? result;
            try
            {
                result = JsonSerializer.Deserialize<ActionFolderListResponse>(body, JsonOpts);
            }
            catch (JsonException ex)
            {
                Console.WriteLine($"  [WARN] Could not parse folder response for action {actionId}: {ex.Message}");
                return folders;
            }

            if (result is null || result.Folders.Count == 0)
                break;

            folders.AddRange(result.Folders);

            if (result.Meta?.Paging?.ActionFolders is { } paging)
                totalPages = paging.PageCount > 0 ? paging.PageCount : 1;
            else
                break;

            page++;
        }

        return folders;
    }

    // ── Participant lookup ────────────────────────────────────────────────────

    // Global cache: participant IDs never change within a run.
    // Null value means we already tried and got no result (avoids repeated 404s).
    private readonly Dictionary<string, string?> _participantCache =
        new(StringComparer.Ordinal);

    /// <summary>
    /// Returns the display name for a participant by ID, e.g. "Lawson, Patrice".
    /// Results are cached for the lifetime of this client instance.
    /// Returns null if the participant cannot be resolved.
    /// </summary>
    public async Task<string?> GetParticipantNameAsync(
        string? participantId,
        CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(participantId))
            return null;

        if (_participantCache.TryGetValue(participantId, out string? cached))
            return cached;

        string url = $"rest/participants/{Uri.EscapeDataString(participantId)}";

        try
        {
            var response = await _http.GetAsync(url, ct);
            if (!response.IsSuccessStatusCode)
            {
                Console.WriteLine(
                    $"  [WARN] GET {url} → {(int)response.StatusCode}: " +
                    $"created_by will be empty for participant {participantId}.");
                _participantCache[participantId] = null;
                return null;
            }

            string body = await response.Content.ReadAsStringAsync(ct);
            var result  = JsonSerializer.Deserialize<ParticipantResponse>(body, JsonOpts);
            string? name = result?.Participant?.ResolvedName;
            _participantCache[participantId] = name;
            return name;
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            Console.WriteLine(
                $"  [WARN] Failed to fetch participant {participantId}: {ex.Message}");
            _participantCache[participantId] = null;
            return null;
        }
    }

    // ── Probe ─────────────────────────────────────────────────────────────────

    /// <summary>
    /// Fetches one record from <paramref name="relativeUrl"/> and returns the
    /// pretty-printed JSON.  Use this to verify DTO field name mappings.
    /// </summary>
    public async Task<string> ProbeEndpointAsync(
        string relativeUrl,
        CancellationToken ct = default)
    {
        var    response = await _http.GetAsync(relativeUrl, ct);
        string body     = await response.Content.ReadAsStringAsync(ct);

        try
        {
            using var doc = JsonDocument.Parse(body);
            return JsonSerializer.Serialize(doc, new JsonSerializerOptions { WriteIndented = true });
        }
        catch
        {
            return body;   // Return raw body if it can't be re-serialised.
        }
    }

    public void Dispose() => _http.Dispose();
}
