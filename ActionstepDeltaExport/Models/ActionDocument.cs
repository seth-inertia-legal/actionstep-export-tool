using System.Text.Json;
using System.Text.Json.Serialization;

namespace ActionstepDeltaExport.Models;

/// <summary>
/// A single record returned by GET /api/rest/actiondocuments.
/// Field names verified against live API probe 2026-06-09.
/// </summary>
public class ActionDocument
{
    /// <summary>Actionstep document ID — maps to manifest log_id.</summary>
    [JsonPropertyName("id")]
    public int Id { get; set; }

    /// <summary>Human-readable document name — maps to manifest document_name.</summary>
    [JsonPropertyName("name")]
    public string? Name { get; set; }

    /// <summary>
    /// Stored file name on disk, e.g. "2_20160901_document.docx".
    /// Null for placeholder documents — these are skipped during download.
    /// </summary>
    [JsonPropertyName("fileName")]
    public string? FileName { get; set; }

    /// <summary>
    /// Internal download identifier, e.g. "DL::Actions::2::1".
    /// Passed directly to GET /api/rest/files/{file}?part_number=N.
    /// NOTE: The API field is "file", not "fileIdentifier".
    /// </summary>
    [JsonPropertyName("file")]
    public string? FileIdentifier { get; set; }

    /// <summary>
    /// Total file size in bytes.
    /// Required to calculate the number of 5 MB download chunks.
    /// Zero for placeholder documents.
    /// </summary>
    [JsonPropertyName("fileSize")]
    public long? FileSize { get; set; }

    /// <summary>File extension including dot, e.g. ".docx".</summary>
    [JsonPropertyName("extension")]
    public string? Extension { get; set; }

    /// <summary>Last-modified UTC timestamp — used for delta comparison.</summary>
    [JsonPropertyName("modifiedTimestamp")]
    public DateTimeOffset? ModifiedTimestamp { get; set; }

    /// <summary>Creation UTC timestamp.</summary>
    [JsonPropertyName("createdTimestamp")]
    public DateTimeOffset? CreatedTimestamp { get; set; }

    /// <summary>
    /// Display name of the user who currently has the document checked out,
    /// or null if not checked out.  Used as modified_by; falls back to createdBy.
    /// </summary>
    [JsonPropertyName("checkedOutTo")]
    public string? CheckedOutTo { get; set; }

    /// <summary>
    /// True if the document has been soft-deleted in Actionstep.
    /// The API returns "T" or "F" as a string, not a JSON boolean.
    /// </summary>
    [JsonPropertyName("isDeleted")]
    [JsonConverter(typeof(TFStringBoolConverter))]
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

    /// <summary>
    /// Participant ID of the user who created this document.
    /// Resolved to a display name via GET /api/rest/participants/{id}.
    /// NOTE: modifiedBy is not present in the actiondocuments API response.
    /// </summary>
    [JsonPropertyName("createdBy")]
    public string? CreatedBy { get; set; }
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
    public PagingWrapper? Paging { get; set; }
}

/// <summary>
/// The paging object is keyed by resource name:
/// { "paging": { "actiondocuments": { "recordCount": ..., "pageCount": ... } } }
/// Each resource type has its own key; add new properties here as needed.
/// </summary>
public class PagingWrapper
{
    [JsonPropertyName("actiondocuments")]
    public PagingInfo? ActionDocuments { get; set; }

    [JsonPropertyName("actionfolders")]
    public PagingInfo? ActionFolders { get; set; }
}

public class PagingInfo
{
    /// <summary>Total number of matching records across all pages.</summary>
    [JsonPropertyName("recordCount")]
    public int TotalCount { get; set; }

    [JsonPropertyName("pageCount")]
    public int PageCount { get; set; }

    /// <summary>Current page number (1-based).</summary>
    [JsonPropertyName("page")]
    public int CurrentPage { get; set; }

    [JsonPropertyName("pageSize")]
    public int PageSize { get; set; }
}

// ── Converters ────────────────────────────────────────────────────────────────

/// <summary>
/// Handles Actionstep's non-standard boolean encoding:
/// the API sends "T" or "F" as a JSON string instead of true/false.
/// </summary>
public sealed class TFStringBoolConverter : JsonConverter<bool>
{
    public override bool Read(
        ref Utf8JsonReader reader,
        Type typeToConvert,
        JsonSerializerOptions options)
    {
        return reader.TokenType switch
        {
            JsonTokenType.String => "T".Equals(reader.GetString()?.Trim(),
                                        StringComparison.OrdinalIgnoreCase),
            JsonTokenType.True   => true,
            JsonTokenType.False  => false,
            _                    => false
        };
    }

    public override void Write(Utf8JsonWriter writer, bool value, JsonSerializerOptions options)
        => writer.WriteStringValue(value ? "T" : "F");
}
