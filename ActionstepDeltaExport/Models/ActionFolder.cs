using System.Text.Json;
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
    /// <summary>
    /// Actionstep returns a JSON array when multiple folders exist, but a bare
    /// object when there is exactly one.  SingleOrArrayConverter handles both.
    /// </summary>
    [JsonPropertyName("actionfolders")]
    [JsonConverter(typeof(SingleOrArrayConverter<ActionFolder>))]
    public List<ActionFolder> Folders { get; set; } = new();

    [JsonPropertyName("meta")]
    public ResponseMeta? Meta { get; set; }
}

// ── Converter ─────────────────────────────────────────────────────────────────

/// <summary>
/// Deserialises a JSON value that is either a single object or an array of
/// objects into a <see cref="List{T}"/>.  Actionstep uses single-object
/// responses when a collection contains exactly one element.
/// </summary>
public sealed class SingleOrArrayConverter<T> : JsonConverter<List<T>>
{
    public override List<T> Read(
        ref Utf8JsonReader reader,
        Type typeToConvert,
        JsonSerializerOptions options)
    {
        return reader.TokenType switch
        {
            JsonTokenType.StartArray  => JsonSerializer.Deserialize<List<T>>(ref reader, options) ?? new(),
            JsonTokenType.StartObject => WrapSingle(ref reader, options),
            JsonTokenType.Null        => new(),
            _                         => new()
        };
    }

    private static List<T> WrapSingle(ref Utf8JsonReader reader, JsonSerializerOptions options)
    {
        var item = JsonSerializer.Deserialize<T>(ref reader, options);
        return item is null ? new() : new() { item };
    }

    public override void Write(Utf8JsonWriter writer, List<T> value, JsonSerializerOptions options)
        => JsonSerializer.Serialize(writer, value, options);
}
