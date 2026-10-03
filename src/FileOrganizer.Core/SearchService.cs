namespace FileOrganizer.Core;

/// <summary>Fast local search (§9). Uses lazy enumeration so results stream in without loading whole trees into memory.</summary>
public static class SearchService
{
    public static IEnumerable<FileInfo> Search(string rootFolder, SearchQuery query, CancellationToken cancellationToken = default)
    {
        var root = Path.GetFullPath(Environment.ExpandEnvironmentVariables(rootFolder));
        if (!Directory.Exists(root))
            throw new DirectoryNotFoundException($"Folder not found: {root}");

        var option = query.Recursive ? SearchOption.AllDirectories : SearchOption.TopDirectoryOnly;
        var ext = query.Extension.TrimStart('.');

        foreach (var path in Directory.EnumerateFiles(root, "*", option))
        {
            cancellationToken.ThrowIfCancellationRequested();
            FileInfo file;
            try { file = new FileInfo(path); }
            catch (IOException) { continue; }

            if (!string.IsNullOrWhiteSpace(query.NameContains) &&
                !file.Name.Contains(query.NameContains, StringComparison.OrdinalIgnoreCase))
                continue;
            if (!string.IsNullOrWhiteSpace(ext) &&
                !string.Equals(file.Extension.TrimStart('.'), ext, StringComparison.OrdinalIgnoreCase))
                continue;
            if (query.MinSizeBytes is { } min && file.Length < min) continue;
            if (query.MaxSizeBytes is { } max && file.Length > max) continue;
            if (query.ModifiedAfter is { } after && file.LastWriteTime < after) continue;
            if (query.ModifiedBefore is { } before && file.LastWriteTime > before) continue;

            yield return file;
        }
    }
}
