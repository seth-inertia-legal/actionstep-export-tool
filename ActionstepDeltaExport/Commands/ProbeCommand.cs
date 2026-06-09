using ActionstepDeltaExport.Models;
using ActionstepDeltaExport.Services;

namespace ActionstepDeltaExport.Commands;

/// <summary>
/// Authenticates and pretty-prints one raw JSON record from each of the three
/// main resource endpoints — actiondocuments, actions, and actionfolders.
/// Use this to verify DTO field name mappings and discover sideloaded shapes
/// (e.g. linked.actiontypes) before running a full export.
/// </summary>
public static class ProbeCommand
{
    private static readonly (string Url, string Label)[] Endpoints =
    [
        ("rest/actiondocuments?pageSize=1", "GET /api/rest/actiondocuments?pageSize=1"),
        ("rest/actions?pageSize=1",         "GET /api/rest/actions?pageSize=1"),
        ("rest/actionfolders?pageSize=1",   "GET /api/rest/actionfolders?pageSize=1"),
        ("rest/actiontypes/6",              "GET /api/rest/actiontypes/6  (sample type ID)"),
    ];

    public static async Task<int> RunAsync(
        AppSettings settings,
        CancellationToken ct = default)
    {
        Console.WriteLine("Authenticating...");
        using var auth = new AuthService(settings.Actionstep);
        var token = await auth.GetValidTokenAsync(ct);

        Console.WriteLine($"API endpoint: {token.ApiEndpoint}");
        Console.WriteLine();

        using var client = new ActionstepApiClient(token);

        foreach (var (url, label) in Endpoints)
        {
            Console.WriteLine(new string('─', 72));
            Console.WriteLine($"Probing {label} ...");
            Console.WriteLine(new string('─', 72));
            Console.WriteLine();

            string json = await client.ProbeEndpointAsync(url, ct);
            Console.WriteLine(json);
            Console.WriteLine();
        }

        return 0;
    }
}
