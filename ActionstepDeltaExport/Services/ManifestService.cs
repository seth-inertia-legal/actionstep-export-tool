using System.Globalization;
using CsvHelper;
using CsvHelper.Configuration;
using ActionstepDeltaExport.Models;

namespace ActionstepDeltaExport.Services;

/// <summary>
/// Handles reading and writing the export manifest CSV.
/// </summary>
public static class ManifestService
{
    // Datetime formats present in the manifest.
    // Covers plain timestamps, high-precision timestamps, and timezone-offset variants
    // such as "2016-09-02 13:32:53+00" exported by Actionstep.
    private static readonly string[] DateFormats =
    {
        "yyyy-MM-dd HH:mm:ss.fffffff",
        "yyyy-MM-dd HH:mm:ss.ffffff",
        "yyyy-MM-dd HH:mm:ss.fffff",
        "yyyy-MM-dd HH:mm:ss.ffff",
        "yyyy-MM-dd HH:mm:ss.fff",
        "yyyy-MM-dd HH:mm:ss",
        "yyyy-MM-dd HH:mm:sszz",    // e.g. 2016-09-02 13:32:53+00
        "yyyy-MM-dd HH:mm:sszzz",   // e.g. 2016-09-02 13:32:53+00:00
        "yyyy-MM-dd HH:mm:ss.ffffffzz",
        "yyyy-MM-dd HH:mm:ss.ffffffzzz",
        "yyyy-MM-dd"
    };

    // ── Read ──────────────────────────────────────────────────────────────────

    /// <summary>
    /// Loads all records from the manifest at <paramref name="path"/>.
    /// Returns an empty list if the file does not exist.
    /// </summary>
    public static List<ManifestRecord> Load(string path)
    {
        if (!File.Exists(path))
            return new List<ManifestRecord>();

        var config = new CsvConfiguration(CultureInfo.InvariantCulture)
        {
            HasHeaderRecord  = true,
            MissingFieldFound = null,   // Tolerate rows with fewer columns.
            BadDataFound      = null,   // Skip rows with unrecoverable parse errors.
        };

        using var reader = new StreamReader(path);
        using var csv    = new CsvReader(reader, config);

        csv.Context.RegisterClassMap<ManifestRecordMap>();

        // Treat the literal string "NULL" as a null value for all nullable types.
        var nullStr = new[] { "NULL", "null", "" };
        csv.Context.TypeConverterOptionsCache.GetOptions<string?>().NullValues.AddRange(nullStr);
        csv.Context.TypeConverterOptionsCache.GetOptions<DateTime?>().NullValues.AddRange(nullStr);
        csv.Context.TypeConverterOptionsCache.GetOptions<int?>().NullValues.AddRange(nullStr);

        // Register all expected datetime formats and adjust to UTC when a
        // timezone offset is present (e.g. "2016-09-02 13:32:53+00").
        var dtOpts = csv.Context.TypeConverterOptionsCache.GetOptions<DateTime?>();
        dtOpts.Formats       = DateFormats;
        dtOpts.DateTimeStyle = DateTimeStyles.AdjustToUniversal | DateTimeStyles.AssumeUniversal;

        var dtOptsNonNull = csv.Context.TypeConverterOptionsCache.GetOptions<DateTime>();
        dtOptsNonNull.Formats       = DateFormats;
        dtOptsNonNull.DateTimeStyle = DateTimeStyles.AdjustToUniversal | DateTimeStyles.AssumeUniversal;

        return csv.GetRecords<ManifestRecord>().ToList();
    }

    // ── Write ─────────────────────────────────────────────────────────────────

    /// <summary>
    /// Writes <paramref name="records"/> to a new CSV at <paramref name="path"/>.
    /// Parent directories are created automatically.
    /// </summary>
    public static void Write(string path, IEnumerable<ManifestRecord> records)
    {
        string? dir = Path.GetDirectoryName(path);
        if (!string.IsNullOrEmpty(dir))
            Directory.CreateDirectory(dir);

        var config = new CsvConfiguration(CultureInfo.InvariantCulture)
        {
            HasHeaderRecord = true
        };

        using var writer = new StreamWriter(path, append: false);
        using var csv    = new CsvWriter(writer, config);

        csv.Context.RegisterClassMap<ManifestRecordMap>();

        // Write null DateTime? as the string "NULL" to match the source manifest.
        csv.Context.TypeConverterOptionsCache.GetOptions<DateTime?>().NullValues.Add("NULL");
        csv.Context.TypeConverterOptionsCache.GetOptions<DateTime?>().Formats =
            new[] { "yyyy-MM-dd HH:mm:ss.fffffff" };
        csv.Context.TypeConverterOptionsCache.GetOptions<DateTime>().Formats =
            new[] { "yyyy-MM-dd HH:mm:ss.fffffff" };

        // Write null strings as "NULL".
        csv.Context.TypeConverterOptionsCache.GetOptions<string?>().NullValues.Add("NULL");

        csv.WriteRecords(records);
    }

    // ── Helpers ───────────────────────────────────────────────────────────────

    /// <summary>
    /// Returns the most recent <c>last_modified</c> value found in the manifest,
    /// or <see langword="null"/> if no records have a timestamp.
    /// </summary>
    public static DateTime? GetMaxLastModified(IEnumerable<ManifestRecord> records)
    {
        var dates = records
            .Where(r => r.LastModified.HasValue)
            .Select(r => r.LastModified!.Value)
            .ToList();

        return dates.Count > 0 ? dates.Max() : null;
    }
}
