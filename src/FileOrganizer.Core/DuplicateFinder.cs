using System.Security.Cryptography;

namespace FileOrganizer.Core;

/// <summary>
/// 🔄 Find Duplicates (§8): exact duplicates only — same size, then same SHA-256 content hash.
/// Files are never deleted here; the caller confirms, and deletion (if chosen) goes through the UI.
/// </summary>
public static class DuplicateFinder
{
    public static List<DuplicateGroup> FindDuplicates(
        string rootFolder,
        bool recursive = true,
        IProgress<(int Scanned, string CurrentFile)>? progress = null,
        CancellationToken cancellationToken = default)
    {
        var root = Path.GetFullPath(Environment.ExpandEnvironmentVariables(rootFolder));
        if (!Directory.Exists(root))
            throw new DirectoryNotFoundException($"Folder not found: {root}");

        var option = recursive ? SearchOption.AllDirectories : SearchOption.TopDirectoryOnly;
        var bySize = new Dictionary<long, List<string>>();
        var scanned = 0;

        foreach (var path in Directory.EnumerateFiles(root, "*", option))
        {
            cancellationToken.ThrowIfCancellationRequested();
            try
            {
                var length = new FileInfo(path).Length;
                if (!bySize.TryGetValue(length, out var list))
                    bySize[length] = list = new List<string>();
                list.Add(path);
                progress?.Report((++scanned, path));
            }
            catch (IOException) { /* file vanished mid-scan; skip it */ }
            catch (UnauthorizedAccessException) { /* skip unreadable files */ }
        }

        var groups = new List<DuplicateGroup>();
        foreach (var (size, candidates) in bySize.Where(kv => kv.Value.Count > 1))
        {
            var byHash = new Dictionary<string, DuplicateGroup>(StringComparer.OrdinalIgnoreCase);
            foreach (var path in candidates)
            {
                cancellationToken.ThrowIfCancellationRequested();
                try
                {
                    var hash = ComputeHash(path);
                    if (!byHash.TryGetValue(hash, out var group))
                        byHash[hash] = group = new DuplicateGroup { Hash = hash, SizeBytes = size };
                    group.Files.Add(path);
                }
                catch (IOException) { /* skip */ }
                catch (UnauthorizedAccessException) { /* skip */ }
            }
            groups.AddRange(byHash.Values.Where(g => g.Files.Count > 1));
        }

        return groups.OrderByDescending(g => g.WastedBytes).ToList();
    }

    private static string ComputeHash(string path)
    {
        using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read, 81920, FileOptions.SequentialScan);
        return Convert.ToHexString(SHA256.HashData(stream));
    }

    /// <summary>Picks which file of a group to KEEP under a strategy; the rest are the removal candidates.</summary>
    public static string PickKeeper(DuplicateGroup group, DuplicateKeepStrategy strategy)
    {
        var files = group.Files.Select(p => new FileInfo(p)).ToList();
        return strategy switch
        {
            DuplicateKeepStrategy.KeepNewest => files.OrderByDescending(f => f.LastWriteTime).First().FullName,
            DuplicateKeepStrategy.KeepOldest => files.OrderBy(f => f.CreationTime).First().FullName,
            _ => files.First().FullName,
        };
    }
}
