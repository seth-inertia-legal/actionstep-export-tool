using System.Text.Json.Serialization;

namespace ActionstepDeltaExport.Models;

/// <summary>
/// A single folder returned by GET /api/rest/actionfolders.
/// Folders are a flat list — each has an optional parentFolder link
/// that must be traversed to reconstruct the full path.
/// </summary>
public class ActionFolder
{
    [JsonPropertyName("id")]
    public int Id { get; set; }

    [JsonPropertyName("name")]
    public string? Name { get; set; }

    [JsonPropertyName("links")]
    public ActionFolderLinks? Links { get; set; }
}

public class ActionFolderLinks
{
    /// <summary>The matter (action) this folder belongs to.</summary>
    [JsonPropertyName("action")]
    public string? Action { get; set; }

    /// <summary>
    /// The parent folder ID, or null if this is a root-level folder.
    /// May come back as a string number or null from the API.
    /// </summary>
    [JsonPropertyName("parentFolder")]
    public string? ParentFolder { get; set; }
}

// ── API response envelope ─────────────────────────────────────────────────────

public class ActionFolderListResponse
{
    [JsonPropertyName("actionfolders")]
    public List<ActionFolder> Folders { get; set; } = new();

    [JsonPropertyName("meta")]
    public ResponseMeta? Meta { get; set; }
}
