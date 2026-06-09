using System.CommandLine;
using Microsoft.Extensions.Configuration;
using ActionstepDeltaExport.Commands;
using ActionstepDeltaExport.Models;
using ActionstepDeltaExport.Services;
using ActionstepDeltaExport.Services;

// ── Configuration ─────────────────────────────────────────────────────────────

var configuration = new ConfigurationBuilder()
    .SetBasePath(AppContext.BaseDirectory)
    .AddJsonFile("appsettings.json", optional: false, reloadOnChange: false)
    .Build();

var settings = configuration.Get<AppSettings>()
    ?? throw new InvalidOperationException("Failed to bind appsettings.json to AppSettings.");

// ── CLI definition ────────────────────────────────────────────────────────────

var rootCommand = new RootCommand("Actionstep delta document export tool.");

// ── export command ────────────────────────────────────────────────────────────

var exportCommand = new Command("export",
    "Download documents created or modified since the configured SinceDate.");

// Optional overrides for the three most common settings.
var sinceOpt      = new Option<string?>("--since",
    "Override SinceDate from appsettings.json (e.g. 2026-05-23).");
var manifestOpt   = new Option<string?>("--manifest",
    "Override ManifestPath from appsettings.json.");
var outputRootOpt = new Option<string?>("--output",
    "Override OutputRoot from appsettings.json.");

exportCommand.AddOption(sinceOpt);
exportCommand.AddOption(manifestOpt);
exportCommand.AddOption(outputRootOpt);

exportCommand.SetHandler(async (string? since, string? manifest, string? output) =>
{
    // Apply any CLI overrides.
    if (!string.IsNullOrWhiteSpace(since))   settings.Export.SinceDate    = since;
    if (!string.IsNullOrWhiteSpace(manifest)) settings.Export.ManifestPath = manifest;
    if (!string.IsNullOrWhiteSpace(output))  settings.Export.OutputRoot   = output;

    // If SinceDate is still empty, derive it from the manifest's max last_modified.
    if (string.IsNullOrWhiteSpace(settings.Export.SinceDate))
    {
        if (string.IsNullOrWhiteSpace(settings.Export.ManifestPath) ||
            !File.Exists(settings.Export.ManifestPath))
        {
            Console.Error.WriteLine(
                "SinceDate is not configured and no existing manifest was found. " +
                "Provide --since or set Export.SinceDate in appsettings.json.");
            Environment.Exit(1);
        }

        var existingRecords = ManifestService.Load(settings.Export.ManifestPath);
        var maxDate = ManifestService.GetMaxLastModified(existingRecords);

        if (maxDate is null)
        {
            Console.Error.WriteLine(
                "Could not determine SinceDate from the manifest " +
                "(no last_modified values found). Provide --since explicitly.");
            Environment.Exit(1);
        }

        settings.Export.SinceDate = maxDate.Value.ToString("o");
        Console.WriteLine(
            $"SinceDate derived from manifest: {maxDate.Value:yyyy-MM-dd HH:mm:ss} UTC");
    }

    int exitCode = await ExportCommand.RunAsync(settings);
    Environment.Exit(exitCode);
},
sinceOpt, manifestOpt, outputRootOpt);

// ── probe command ─────────────────────────────────────────────────────────────

var probeCommand = new Command("probe",
    "Authenticate and print a raw JSON sample from GET /api/rest/actiondocuments?pageSize=1. " +
    "Use this to verify field name mappings before the first export.");

probeCommand.SetHandler(async () =>
{
    int exitCode = await ProbeCommand.RunAsync(settings);
    Environment.Exit(exitCode);
});

// ── Wire up and invoke ────────────────────────────────────────────────────────

rootCommand.AddCommand(exportCommand);
rootCommand.AddCommand(probeCommand);

return await rootCommand.InvokeAsync(args);
