using System.Text.Json.Serialization;

namespace ActionstepDeltaExport.Models;

/// <summary>
/// A participant (system user) returned by GET /api/rest/participants/{id}.
/// Used to resolve the createdBy participant ID on actiondocuments to a display name.
/// </summary>
public class Participant
{
    [JsonPropertyName("id")]
    public int Id { get; set; }

    /// <summary>
    /// Full display name, e.g. "Lawson, Patrice".
    /// Present on most participant records.
    /// </summary>
    [JsonPropertyName("displayName")]
    public string? DisplayName { get; set; }

    [JsonPropertyName("firstName")]
    public string? FirstName { get; set; }

    [JsonPropertyName("lastName")]
    public string? LastName { get; set; }

    /// <summary>
    /// Returns the best available name: displayName, then "LastName, FirstName",
    /// then null if nothing is populated.
    /// </summary>
    public string? ResolvedName
    {
        get
        {
            if (!string.IsNullOrWhiteSpace(DisplayName))
                return DisplayName;

            string last  = LastName?.Trim()  ?? "";
            string first = FirstName?.Trim() ?? "";

            if (last.Length > 0 && first.Length > 0) return $"{last}, {first}";
            if (last.Length > 0)  return last;
            if (first.Length > 0) return first;
            return null;
        }
    }
}

public class ParticipantResponse
{
    /// <summary>
    /// Single-object response: {"participants": { ... }}.
    /// </summary>
    [JsonPropertyName("participants")]
    public Participant? Participant { get; set; }
}
