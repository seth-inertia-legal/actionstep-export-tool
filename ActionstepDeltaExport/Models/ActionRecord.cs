using CsvHelper.Configuration;

namespace ActionstepDeltaExport.Models;

/// <summary>
/// One row in the actions CSV output produced by --include actions.
/// </summary>
public class ActionRecord
{
    public int     ActionId       { get; set; }
    public string? ActionName     { get; set; }
    public string? ActionTypeId   { get; set; }
    public string? FileReference  { get; set; }
    public string? ActionTypeName { get; set; }
}

public sealed class ActionRecordMap : ClassMap<ActionRecord>
{
    public ActionRecordMap()
    {
        Map(m => m.ActionId).Name("action_id");
        Map(m => m.ActionName).Name("action_name");
        Map(m => m.ActionTypeId).Name("action_type_id");
        Map(m => m.FileReference).Name("file_reference");
        Map(m => m.ActionTypeName).Name("action_type_name");
    }
}
