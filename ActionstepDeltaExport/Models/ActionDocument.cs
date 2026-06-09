using System.Text.Json.Serialization;

namespace ActionstepDeltaExport.Models;

// ─────────────────────────────────────────────────────────────────────────────
// IMPORTANT: Run  `ActionstepDeltaExport.exe probe`  before the first export.
// The probe prints the raw JSON from the actiondocuments endpoint so you can
// verify that the [JsonPropertyName] values below match the actual API field
// names.  If any names differ, update the attributes and rebuild.
// ─────────────────────────────────────────────────────────────────────────────

/// <summary>
/// A single record returned by GET /api/rest/actiondocuments.
/// </summary>
public class ActionDocument
{
    /// <summary>Actionstep document ID — maps to manifest log_id.</summary>
    [JsonPropertyName("id")]
    public string Id { get; set; } = string.Empty;

    /// <summary>Human-readable document name — maps to manifest document_name.</summary>
    [JsonPropertyName("name")]
    public string? Name { get; set; }

    /// <summary>
    /// Stored file name on disk, e.g. "123_20230101_document.pdf".
    /// Maps to manifest file_name.
    /// Null for placeholder (empty) documents — these are skipped during download.
    /// </summary>
    [JsonPropertyName("fileName")]
    public string? FileName { get; set; }

    /// <summary>
    /// Internal download identifier, e.g. "DL::Actions::1570803::12537;".
    /// Passed directly to GET /api/rest/files/{fileIdentifier}?part_number=N.
    /// </summary>
    [JsonPropertyName("fileIdentifier")]
    public string? FileIdentifier { get; set; }

    /// <summary>
    /// Total file size in bytes.
    /// Required to calculate the number of 5 MB download chunks.
    /// Zero for placeholder documents.
    /// </summary>
    [JsonPropertyName("fileSize")]
    public long FileSize { get; set; }

    /// <summary>Last-modified UTC timestamp — used for delta comparison.</summary>
    [JsonPropertyName("modifiedTimestamp")]
    public DateTimeOffset? ModifiedTimestamp { get; set; }

    /// <summary>Creation UTC timestamp.</summary>
    [JsonPropertyName("createdTimestamp")]
    public DateTimeOffset? CreatedTimestamp { get; set; }

    /// <summary>True if the document has been soft-deleted in Actionstep.</summary>
    [JsonPropertyName("isDeleted")]
    public bool IsDeleted { get; set; }

    [JsonPropertyName("links")]
    public ActionDocumentLinks? Links { get; set; }
}

public class ActionDocumentLinks
{
    /// <summary>Parent matter (action) ID — maps to manifest action_id.</summary>
    [JsonPropertyName("action")]
    public string? Action { get; set; }

    /// <summary>Parent folder ID — maps to manifest folder_id.</summary>
    [JsonPropertyName("folder")]
    public string? Folder { get; set; }
}

// ── API response envelope ─────────────────────────────────────────────────────

public class ActionDocumentListResponse
{
    [JsonPropertyName("actiondocuments")]
    public List<ActionDocument> Documents { get; set; } = new();

    [JsonPropertyName("meta")]
    public ResponseMeta? Meta { get; set; }
}

public class ResponseMeta
{
    [JsonPropertyName("paging")]
    public PagingInfo? Paging { get; set; }
}

public class PagingInfo
{
    [JsonPropertyName("totalCount")]
    public int TotalCount { get; set; }

    [JsonPropertyName("pageCount")]
    public int PageCount { get; set; }

    [JsonPropertyName("currentPage")]
    public int CurrentPage { get; set; }

    [JsonPropertyName("pageSize")]
    public int PageSize { get; set; }
}
