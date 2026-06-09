using CsvHelper;
using CsvHelper.Configuration;
using CsvHelper.TypeConversion;

namespace ActionstepDeltaExport.Models;

/// <summary>
/// Represents a single row in the export manifest CSV.
/// Column order and names match the source manifest exactly.
/// </summary>
public class ManifestRecord
{
    public int       LogId              { get; set; }
    public int       ActionId           { get; set; }
    public string?   DocumentName       { get; set; }
    public string?   TemplateId         { get; set; }
    public string?   FileName           { get; set; }
    public string?   Directory          { get; set; }
    public string?   FolderId           { get; set; }
    public string?   FileType           { get; set; }
    public string?   CreatedBy          { get; set; }
    public string?   ModifiedBy         { get; set; }
    public DateTime? CreatedDate        { get; set; }
    public DateTime? LastModified       { get; set; }
    public DateTime? DocumentTimestamp  { get; set; }
    public bool      IsDeleted          { get; set; }
}

/// <summary>
/// CsvHelper class map that binds ManifestRecord properties to the manifest CSV columns.
/// </summary>
public sealed class ManifestRecordMap : ClassMap<ManifestRecord>
{
    public ManifestRecordMap()
    {
        Map(m => m.LogId).Name("log_id");
        Map(m => m.ActionId).Name("action_id");
        Map(m => m.DocumentName).Name("document_name");
        Map(m => m.TemplateId).Name("template_id");
        Map(m => m.FileName).Name("file_name");
        Map(m => m.Directory).Name("directory");
        Map(m => m.FolderId).Name("folder_id");
        Map(m => m.FileType).Name("file_type");
        Map(m => m.CreatedBy).Name("created_by");
        Map(m => m.ModifiedBy).Name("modified_by");
        Map(m => m.CreatedDate).Name("created_date");
        Map(m => m.LastModified).Name("last_modified");
        Map(m => m.DocumentTimestamp).Name("document_timestamp");
        Map(m => m.IsDeleted).Name("is_deleted").TypeConverter<TFBoolConverter>();
    }
}

/// <summary>
/// Converts the manifest's "T"/"F" boolean representation to/from C# bool.
/// </summary>
public sealed class TFBoolConverter : DefaultTypeConverter
{
    public override object ConvertFromString(string? text, IReaderRow row, MemberMapData memberMapData)
        => "T".Equals(text?.Trim(), StringComparison.OrdinalIgnoreCase);

    public override string ConvertToString(object? value, IWriterRow row, MemberMapData memberMapData)
        => value is true ? "T" : "F";
}
