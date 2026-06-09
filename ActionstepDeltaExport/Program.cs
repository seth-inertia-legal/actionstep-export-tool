using System.CommandLine;
using Microsoft.Extensions.Configuration;
using ActionstepDeltaExport.Commands;
using ActionstepDeltaExport.Models;
using ActionstepDeltaExport.Services;

// ── Configuration ─────────────────────────────────────────────────────────────

var configuration = new ConfigurationBuilder()
    .SetBasePath(AppContext.BaseDirectory)
    .AddJsonFile("appsettings.json", optional: false, reloadOnChange: false)
    .AddJsonFile("appsettings.Local.json", optional: true,  reloadOnChange: false)
    .Build();

var settings = configuration.Get<AppSettings>()
    ?? throw new InvalidOperationException("Failed to bind appsettings.json to AppSettings.");

// ── CLI definition ────────────────────────────────────────────────────────────

var rootCommand = new RootCommand("Actionstep document export tool.");

// ── export command ────────────────────────────────────────────────────────────

var exportCommand = new Command("export",
    "Export documents, actions, and/or folders from Actionstep.");

var sinceOpt = new Option<string?>("--since",
    "Override SinceDate from appsettings.json (e.g. 2026-05-23). Applies to documents only.");

var manifestOpt = new Option<string?>("--manifest",
    "Override ManifestPath from appsettings.json.");

var outputRootOpt = new Option<string?>("--output",
    "Override OutputRoot from appsettings.json.");

var includeOpt = new Option<string>(
    "--include",
    "Comma-separated list of what to export: documents, actions, folders. " +
    "At least one value required. Example: --include documents,actions")
{
    IsRequired = true
};

var downloadOpt = new Option<bool>(
    "--download",
    "Download document files to disk. Without this flag, only the audit CSV is written " +
    "(applies to documents only; actions and folders always write CSVs).");

exportCommand.AddOption(sinceOpt);
exportCommand.AddOption(manifestOpt);
exportCommand.AddOption(outputRootOpt);
exportCommand.AddOption(includeOpt);
exportCommand.AddOption(downloadOpt);

exportCommand.SetHandler(
    async (string? since, string? manifest, string? output, string include, bool download) =>
    {
        // ── Parse and validate --include ──────────────────────────────────────

        var validValues = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
            { "documents", "actions", "folders" };

        var includeSet = include
            .Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Select(s => s.ToLowerInvariant())
            .ToHashSet();

        var invalid = includeSet.Except(validValues, StringComparer.OrdinalIgnoreCase).ToList();
        if (invalid.Count > 0)
        {
            Console.Error.WriteLine(
                $"Invalid --include value(s): {string.Join(", ", invalid)}. " +
                "Valid values: documents, actions, folders");
            Environment.Exit(1);
        }

        if (includeSet.Count == 0)
        {
            Console.Error.WriteLine(
                "--include requires at least one value: documents, actions, folders");
            Environment.Exit(1);
        }

        // ── Apply CLI overrides ───────────────────────────────────────────────

        if (!string.IsNullOrWhiteSpace(since))    settings.Export.SinceDate    = since;
        if (!string.IsNullOrWhiteSpace(manifest)) settings.Export.ManifestPath = manifest;
        if (!string.IsNullOrWhiteSpace(output))   settings.Export.OutputRoot   = output;

        // ── Derive SinceDate from manifest (documents only) ───────────────────

        if (includeSet.Contains("documents") &&
            string.IsNullOrWhiteSpace(settings.Export.SinceDate))
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

        // ── Run ───────────────────────────────────────────────────────────────

        try
        {
            int exitCode = await ExportCommand.RunAsync(settings, includeSet, download);
            Environment.Exit(exitCode);
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine();
            Console.Error.WriteLine($"Fatal error: {ex.Message}");
            Console.Error.WriteLine(ex.StackTrace);
            Environment.Exit(99);
        }
    },
    sinceOpt, manifestOpt, outputRootOpt, includeOpt, downloadOpt);

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
