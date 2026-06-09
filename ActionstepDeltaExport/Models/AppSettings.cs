namespace ActionstepDeltaExport.Models;

public class AppSettings
{
    public ActionstepSettings Actionstep { get; set; } = new();
    public ExportSettings     Export     { get; set; } = new();
}

public class ActionstepSettings
{
    public string ClientId     { get; set; } = string.Empty;
    public string ClientSecret { get; set; } = string.Empty;
    public string AuthorizeUrl { get; set; } = "https://go.actionstep.com/api/oauth/authorize";
    public string TokenUrl     { get; set; } = "https://api.actionstep.com/api/oauth/token";
    public string Scopes       { get; set; } = "openid";

    /// <summary>
    /// Port for the local OAuth callback listener.
    /// Set to 0 (default) to pick a random available port automatically.
    /// </summary>
    public int CallbackPort { get; set; } = 0;

    /// <summary>
    /// Optional override for the API endpoint returned by the token response.
    /// Set this to force a specific regional endpoint, e.g.
    /// "https://us-west-2.actionstep.com/api/"
    /// Leave empty to use the endpoint returned by Actionstep automatically.
    /// </summary>
    public string ApiEndpointOverride { get; set; } = string.Empty;
}

public class ExportSettings
{
    /// <summary>Path to the existing manifest CSV from the previous export.</summary>
    public string ManifestPath { get; set; } = string.Empty;

    /// <summary>Root directory where downloaded files will be saved (organised by action ID).</summary>
    public string OutputRoot { get; set; } = string.Empty;

    /// <summary>
    /// Optional cutoff date (YYYY-MM-DD).
    /// If omitted the tool derives the date from the maximum last_modified value in the manifest.
    /// </summary>
    public string SinceDate { get; set; } = string.Empty;
}
