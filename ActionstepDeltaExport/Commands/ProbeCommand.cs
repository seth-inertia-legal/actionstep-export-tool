using ActionstepDeltaExport.Models;
using ActionstepDeltaExport.Services;

namespace ActionstepDeltaExport.Commands;

/// <summary>
/// Runs a single API call to GET /api/rest/actiondocuments?pageSize=1 and
/// pretty-prints the raw JSON response.  Use this before the first export to
/// verify that JsonPropertyName values in ActionDocument.cs match the live API.
/// </summary>
public static class ProbeCommand
{
    public static async Task<int> RunAsync(
        AppSettings settings,
        CancellationToken ct = default)
    {
        Console.WriteLine("Authenticating...");
        using var auth = new AuthService(settings.Actionstep);
        var token = await auth.GetValidTokenAsync(ct);

        Console.WriteLine($"API endpoint: {token.ApiEndpoint}");
        Console.WriteLine();
        Console.WriteLine("Probing GET /api/rest/actiondocuments?pageSize=1 ...");
        Console.WriteLine();

        using var client = new ActionstepApiClient(token);
        string json = await client.ProbeAsync(ct);

        Console.WriteLine(json);
        return 0;
    }
}
