using CsvHelper.Configuration;

namespace ActionstepDeltaExport.Models;

/// <summary>
/// One row in the folders CSV output produced by --include folders.
/// </summary>
public class FolderRecord
{
    public int     FolderId      { get; set; }
    public string? ActionId      { get; set; }
    public string? Name          { get; set; }
    public string? ParentFolderId { get; set; }
    public string? FolderPath    { get; set; }
}

public sealed class FolderRecordMap : ClassMap<FolderRecord>
{
    public FolderRecordMap()
    {
        Map(m => m.FolderId).Name("folder_id");
        Map(m => m.ActionId).Name("action_id");
        Map(m => m.Name).Name("name");
        Map(m => m.ParentFolderId).Name("parent_folder_id");
        Map(m => m.FolderPath).Name("folder_path");
    }
}
