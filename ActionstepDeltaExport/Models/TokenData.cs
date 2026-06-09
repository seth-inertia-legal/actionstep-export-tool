namespace ActionstepDeltaExport.Models;

/// <summary>
/// OAuth token state persisted to tokens.json after a successful authentication.
/// </summary>
public class TokenData
{
    public string          AccessToken  { get; set; } = string.Empty;
    public string          RefreshToken { get; set; } = string.Empty;

    /// <summary>UTC time at which the access token expires.</summary>
    public DateTimeOffset  ExpiresAt    { get; set; }

    /// <summary>
    /// The API base URL returned by Actionstep in the token response (region-specific).
    /// All subsequent API requests must use this endpoint.
    /// Example: "https://ap-southeast-2.actionstep.com/api/"
    /// </summary>
    public string          ApiEndpoint  { get; set; } = string.Empty;

    /// <summary>Organisation key returned with the token — used as context for all API calls.</summary>
    public string          OrgKey       { get; set; } = string.Empty;

    /// <summary>
    /// The redirect URI used during the initial OAuth flow.
    /// Must be provided again (unchanged) when refreshing the token.
    /// </summary>
    public string          RedirectUri  { get; set; } = string.Empty;
}
