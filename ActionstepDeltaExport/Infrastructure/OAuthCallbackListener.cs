using System.Net;
using System.Text;

namespace ActionstepDeltaExport.Infrastructure;

/// <summary>
/// Spins up a temporary local HTTP listener to receive the OAuth 2.0 authorization-code
/// callback from Actionstep after the user completes login in their browser.
/// </summary>
public static class OAuthCallbackListener
{
    /// <summary>
    /// Starts listening on the origin derived from <paramref name="callbackUrl"/>,
    /// waits for Actionstep to redirect back with a <c>?code=</c> query parameter,
    /// sends a simple "success" page to the browser, then returns the code.
    /// </summary>
    /// <param name="callbackUrl">
    /// Full callback URL registered in the OAuth request, e.g.
    /// <c>http://localhost:54321/callback</c>.
    /// </param>
    /// <param name="ct">Optional cancellation token.</param>
    /// <returns>The authorization code string.</returns>
    /// <exception cref="InvalidOperationException">
    /// Thrown when Actionstep returns an error or no code is present in the callback.
    /// </exception>
    public static async Task<string> WaitForCallbackAsync(
        string callbackUrl,
        CancellationToken ct = default)
    {
        // HttpListener prefix must cover the entire origin so the OS routes the
        // callback path to us regardless of the trailing slash.
        var uri    = new Uri(callbackUrl);
        var prefix = $"{uri.Scheme}://{uri.Host}:{uri.Port}/";

        using var listener = new HttpListener();
        listener.Prefixes.Add(prefix);
        listener.Start();

        Console.WriteLine($"Listening for OAuth callback on {prefix} ...");

        HttpListenerContext context;
        try
        {
            context = await listener.GetContextAsync().WaitAsync(ct);
        }
        catch (OperationCanceledException)
        {
            listener.Stop();
            throw;
        }

        var qs    = context.Request.QueryString;
        var code  = qs["code"];
        var error = qs["error"];

        // Always respond to the browser so it doesn't hang.
        string html = string.IsNullOrEmpty(error)
            ? "<html><body><h2>Authentication successful!</h2>" +
              "<p>You may close this tab and return to the terminal.</p></body></html>"
            : $"<html><body><h2>Authentication failed.</h2><p>{WebUtility.HtmlEncode(error)}</p></body></html>";

        byte[] responseBytes = Encoding.UTF8.GetBytes(html);
        context.Response.ContentType     = "text/html; charset=utf-8";
        context.Response.ContentLength64 = responseBytes.Length;
        context.Response.StatusCode      = 200;
        await context.Response.OutputStream.WriteAsync(responseBytes, ct);
        context.Response.Close();

        listener.Stop();

        if (!string.IsNullOrEmpty(error))
            throw new InvalidOperationException($"OAuth error returned by Actionstep: {error}");

        if (string.IsNullOrEmpty(code))
            throw new InvalidOperationException(
                "OAuth callback received but no authorization code was present in the query string.");

        return code;
    }
}
