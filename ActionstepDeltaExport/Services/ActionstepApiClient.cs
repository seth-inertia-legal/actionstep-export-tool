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

    public ActionstepApiClient(TokenData token)
    {
        _base = token.ApiEndpoint.TrimEnd('/') + "/";

        _http = new HttpClient { BaseAddress = new Uri(_base) };
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
            if (page > 1)
                Console.WriteLine($"  → Fetching page {page}/{(totalPages == int.MaxValue ? "?" : totalPages.ToString())} ...");

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
                    Console.WriteLine(
                        $"  → {paging.TotalCount:N0} document(s) in delta across " +
                        $"{totalPages} page(s).");
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

    // ── Folder listing ───────────────────────────────────────────────────────

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

    // ── Probe ─────────────────────────────────────────────────────────────────

    /// <summary>
    /// Fetches a single actiondocument record and returns the pretty-printed JSON.
    /// Use this to verify <see cref="ActionDocument"/> field name mappings before
    /// running a full export.
    /// </summary>
    public async Task<string> ProbeAsync(CancellationToken ct = default)
    {
        string url      = "rest/actiondocuments?pageSize=1";
        var    response = await _http.GetAsync(url, ct);
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
