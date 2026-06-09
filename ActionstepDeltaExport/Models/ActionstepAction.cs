using System.Text.Json.Serialization;

namespace ActionstepDeltaExport.Models;

/// <summary>
/// A single matter/action returned by GET /api/rest/actions.
/// </summary>
public class ActionstepAction
{
    [JsonPropertyName("id")]
    public int Id { get; set; }

    [JsonPropertyName("name")]
    public string? Name { get; set; }

    /// <summary>The firm's file reference number, e.g. "2024-001".</summary>
    [JsonPropertyName("reference")]
    public string? Reference { get; set; }

    [JsonPropertyName("links")]
    public ActionstepActionLinks? Links { get; set; }
}

public class ActionstepActionLinks
{
    /// <summary>Action type ID — resolved to a name via GET /api/rest/actiontypes/{id}.</summary>
    [JsonPropertyName("actionType")]
    public string? ActionType { get; set; }
}

// ── API response envelopes ────────────────────────────────────────────────────

public class ActionstepActionListResponse
{
    [JsonPropertyName("actions")]
    public List<ActionstepAction> Actions { get; set; } = new();

    [JsonPropertyName("meta")]
    public ResponseMeta? Meta { get; set; }
}

/// <summary>
/// Single action type record returned by GET /api/rest/actiontypes/{id}.
/// </summary>
public class ActionType
{
    [JsonPropertyName("id")]
    public int Id { get; set; }

    [JsonPropertyName("name")]
    public string? Name { get; set; }
}

public class ActionTypeResponse
{
    [JsonPropertyName("actiontypes")]
    public ActionType? ActionType { get; set; }
}
