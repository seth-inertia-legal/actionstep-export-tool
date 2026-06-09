using System.Diagnostics;
using System.Net;
using System.Net.Sockets;
using System.Text.Json;
using ActionstepDeltaExport.Infrastructure;
using ActionstepDeltaExport.Models;

namespace ActionstepDeltaExport.Services;

/// <summary>
/// Manages OAuth 2.0 authentication against the Actionstep API.
/// On the first run it opens a browser for the user to log in and consent;
/// subsequent runs use the cached tokens, auto-refreshing as needed.
/// </summary>
public sealed class AuthService : IDisposable
{
    private static readonly string TokenFilePath =
        Path.Combine(AppContext.BaseDirectory, "tokens.json");

    private static readonly JsonSerializerOptions JsonOpts =
        new() { WriteIndented = true };

    private readonly ActionstepSettings _cfg;
    private readonly HttpClient         _http = new();

    public AuthService(ActionstepSettings cfg) => _cfg = cfg;

    // ── Public entry point ────────────────────────────────────────────────────

    /// <summary>
    /// Returns a <see cref="TokenData"/> whose access token is valid right now.
    /// Runs the browser flow on first use; auto-refreshes on subsequent runs.
    /// </summary>
    public async Task<TokenData> GetValidTokenAsync(CancellationToken ct = default)
    {
        var stored = TryLoadTokens();

        if (stored is not null)
        {
            // Access token still good (keep a 60-second buffer).
            if (DateTimeOffset.UtcNow < stored.ExpiresAt.AddSeconds(-60))
                return stored;

            // Try a silent refresh.
            try
            {
                Console.WriteLine("Access token expiring — refreshing silently...");
                var refreshed = await RefreshAsync(stored, ct);
                ApplyEndpointOverride(refreshed);
                SaveTokens(refreshed);
                Console.WriteLine("Token refreshed.");
                return refreshed;
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Token refresh failed ({ex.Message}). " +
                                  "Starting interactive login...");
            }
        }

        return await RunBrowserFlowAsync(ct);
    }

    // ── Browser-based OAuth flow ──────────────────────────────────────────────

    private async Task<TokenData> RunBrowserFlowAsync(CancellationToken ct)
    {
        int port        = _cfg.CallbackPort > 0 ? _cfg.CallbackPort : FindFreePort();
        string redirect = $"http://localhost:{port}/callback";
        string authUrl  = BuildAuthorizeUrl(redirect);

        Console.WriteLine();
        Console.WriteLine("Opening browser for Actionstep login...");
        Console.WriteLine("If it does not open automatically, navigate to:");
        Console.WriteLine(authUrl);
        Console.WriteLine();

        OpenBrowser(authUrl);

        string code = await OAuthCallbackListener.WaitForCallbackAsync(redirect, ct);
        Console.WriteLine("Authorization code received — exchanging for tokens...");

        var tokens = await ExchangeCodeAsync(code, redirect, ct);
        tokens.RedirectUri = redirect;   // Persist for future refresh calls.
        ApplyEndpointOverride(tokens);
        SaveTokens(tokens);

        Console.WriteLine($"Authenticated successfully.  Org key: {tokens.OrgKey}");
        Console.WriteLine($"API endpoint: {tokens.ApiEndpoint}");
        return tokens;
    }

    // ── Token operations ──────────────────────────────────────────────────────

    private string BuildAuthorizeUrl(string redirectUri) =>
        $"{_cfg.AuthorizeUrl}" +
        $"?response_type=code" +
        $"&client_id={Uri.EscapeDataString(_cfg.ClientId)}" +
        $"&redirect_uri={Uri.EscapeDataString(redirectUri)}" +
        $"&scope={Uri.EscapeDataString(_cfg.Scopes)}";

    private Task<TokenData> ExchangeCodeAsync(string code, string redirect, CancellationToken ct) =>
        PostTokenAsync(new Dictionary<string, string>
        {
            ["code"]          = code,
            ["client_id"]     = _cfg.ClientId,
            ["client_secret"] = _cfg.ClientSecret,
            ["grant_type"]    = "authorization_code",
            ["redirect_uri"]  = redirect
        }, ct);

    private Task<TokenData> RefreshAsync(TokenData current, CancellationToken ct) =>
        PostTokenAsync(new Dictionary<string, string>
        {
            ["refresh_token"] = current.RefreshToken,
            ["client_id"]     = _cfg.ClientId,
            ["client_secret"] = _cfg.ClientSecret,
            ["grant_type"]    = "refresh_token",
            ["redirect_uri"]  = current.RedirectUri   // Must match original.
        }, ct);

    private async Task<TokenData> PostTokenAsync(
        Dictionary<string, string> form, CancellationToken ct)
    {
        using var request = new HttpRequestMessage(HttpMethod.Post, _cfg.TokenUrl)
        {
            Content = new FormUrlEncodedContent(form)
        };

        var response = await _http.SendAsync(request, ct);
        var body     = await response.Content.ReadAsStringAsync(ct);

        if (!response.IsSuccessStatusCode)
            throw new InvalidOperationException(
                $"Token endpoint returned {(int)response.StatusCode}: {body}");

        using var doc = JsonDocument.Parse(body);
        var root      = doc.RootElement;

        int expiresIn = root.GetProperty("expires_in").GetInt32();

        return new TokenData
        {
            AccessToken  = root.GetProperty("access_token").GetString()!,
            RefreshToken = root.GetProperty("refresh_token").GetString()!,
            ExpiresAt    = DateTimeOffset.UtcNow.AddSeconds(expiresIn),
            ApiEndpoint  = root.GetProperty("api_endpoint").GetString()!,
            OrgKey       = root.GetProperty("orgkey").GetString()!
        };
    }

    // ── Token file I/O ────────────────────────────────────────────────────────

    private static TokenData? TryLoadTokens()
    {
        if (!File.Exists(TokenFilePath)) return null;
        try
        {
            return JsonSerializer.Deserialize<TokenData>(
                File.ReadAllText(TokenFilePath));
        }
        catch
        {
            return null;   // Corrupt file — trigger a fresh login.
        }
    }

    private static void SaveTokens(TokenData data) =>
        File.WriteAllText(TokenFilePath,
            JsonSerializer.Serialize(data, JsonOpts));

    // ── Helpers ───────────────────────────────────────────────────────────────

    private static int FindFreePort()
    {
        using var tmp = new TcpListener(IPAddress.Loopback, 0);
        tmp.Start();
        int port = ((IPEndPoint)tmp.LocalEndpoint).Port;
        tmp.Stop();
        return port;
    }

    private static void OpenBrowser(string url)
    {
        try { Process.Start(new ProcessStartInfo(url) { UseShellExecute = true }); }
        catch { /* User will copy the URL manually. */ }
    }

    /// <summary>
    /// If <see cref="ActionstepSettings.ApiEndpointOverride"/> is set, replaces
    /// the api_endpoint returned by the token response with the configured value.
    /// </summary>
    private void ApplyEndpointOverride(TokenData token)
    {
        if (!string.IsNullOrWhiteSpace(_cfg.ApiEndpointOverride))
        {
            Console.WriteLine(
                $"ApiEndpointOverride applied: {token.ApiEndpoint} → {_cfg.ApiEndpointOverride}");
            token.ApiEndpoint = _cfg.ApiEndpointOverride;
        }
    }

    public void Dispose() => _http.Dispose();
}
