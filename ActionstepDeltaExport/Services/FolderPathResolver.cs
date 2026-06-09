using ActionstepDeltaExport.Models;

namespace ActionstepDeltaExport.Services;

/// <summary>
/// Reconstructs full folder paths from Actionstep's flat folder list.
///
/// Actionstep stores folders as a flat collection where each folder has an
/// optional <c>parentFolder</c> link.  This resolver walks each folder's
/// parent chain to the root and returns a dictionary mapping every folder ID
/// to its full slash-separated path, e.g. "Correspondence/Incoming/2024".
/// </summary>
public static class FolderPathResolver
{
    private const int MaxDepth = 30;   // Cycle / runaway guard.

    /// <summary>
    /// Builds a <c>folderId → full path</c> dictionary from a flat folder list.
    /// </summary>
    public static Dictionary<int, string> BuildPathDictionary(
        IEnumerable<ActionFolder> folders)
    {
        // Index all folders by id for O(1) parent lookup.
        var index = folders
            .Where(f => f.Id > 0)
            .ToDictionary(f => f.Id, f => f);

        // Memoization cache: once a folder's path is resolved, reuse it.
        var cache = new Dictionary<int, string>(index.Count);

        foreach (var folder in index.Values)
            ResolveAndCache(folder.Id, index, cache, depth: 0);

        return cache;
    }

    // ── Private helpers ───────────────────────────────────────────────────────

    private static string ResolveAndCache(
        int folderId,
        Dictionary<int, ActionFolder> index,
        Dictionary<int, string> cache,
        int depth)
    {
        // Already resolved — return the cached value.
        if (cache.TryGetValue(folderId, out string? cached))
            return cached;

        // Cycle / depth guard — return just the name to avoid infinite recursion.
        if (depth > MaxDepth)
        {
            string safeName = index.TryGetValue(folderId, out var sf)
                ? sf.Name ?? folderId.ToString()
                : folderId.ToString();
            cache[folderId] = safeName;
            return safeName;
        }

        if (!index.TryGetValue(folderId, out var folder))
        {
            // Folder id not found in the list (shouldn't happen, but be safe).
            cache[folderId] = folderId.ToString();
            return cache[folderId];
        }

        string name = folder.Name ?? folder.Id.ToString();

        // Walk to parent.
        if (int.TryParse(folder.Links?.ParentFolder, out int parentId) && parentId > 0)
        {
            string parentPath = ResolveAndCache(parentId, index, cache, depth + 1);
            cache[folderId]   = $"{parentPath}/{name}";
        }
        else
        {
            // Root-level folder — no parent.
            cache[folderId] = name;
        }

        return cache[folderId];
    }
}
