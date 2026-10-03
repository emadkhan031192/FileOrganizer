namespace FileOrganizer.Core;

public static class PathHelpers
{
    /// <summary>
    /// Returns <paramref name="desiredPath"/> if free, otherwise "name (1).ext", "name (2).ext", ...
    /// Never overwrites an existing file. A file never conflicts with itself (same-path moves are no-ops upstream).
    /// </summary>
    public static string GetUniqueFilePath(string desiredPath)
    {
        if (!File.Exists(desiredPath))
            return desiredPath;

        var dir = Path.GetDirectoryName(desiredPath) ?? "";
        var stem = Path.GetFileNameWithoutExtension(desiredPath);
        var ext = Path.GetExtension(desiredPath);
        for (var i = 1; i < 10_000; i++)
        {
            var candidate = Path.Combine(dir, $"{stem} ({i}){ext}");
            if (!File.Exists(candidate))
                return candidate;
        }
        throw new IOException($"Could not find a free file name near '{desiredPath}'.");
    }

    /// <summary>Combines folder segments that may use either separator (config files are edited by humans on Windows).</summary>
    public static string Combine(params string[] parts)
    {
        var cleaned = new List<string>();
        for (var i = 0; i < parts.Length; i++)
        {
            if (string.IsNullOrWhiteSpace(parts[i]))
                continue;
            var p = parts[i].Replace('/', Path.DirectorySeparatorChar).Replace('\\', Path.DirectorySeparatorChar);
            // Only continuation segments are trimmed on both ends; trimming the first
            // segment would destroy an absolute root ("/" on Unix, "\\server" or "C:\" edge cases).
            cleaned.Add(i == 0 && cleaned.Count == 0
                ? (p.Length > 1 ? p.TrimEnd(Path.DirectorySeparatorChar) : p)
                : p.Trim(Path.DirectorySeparatorChar));
        }
        return Path.Combine(cleaned.ToArray());
    }

    /// <summary>Path equality using the host filesystem's case rules (Windows: case-insensitive, elsewhere: case-sensitive).</summary>
    public static bool IsSamePath(string a, string b) =>
        string.Equals(Path.GetFullPath(a), Path.GetFullPath(b),
            OperatingSystem.IsWindows() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal);

    /// <summary>Path equality that always ignores case — used to detect case-only renames.</summary>
    public static bool IsSamePathIgnoreCase(string a, string b) =>
        string.Equals(Path.GetFullPath(a), Path.GetFullPath(b), StringComparison.OrdinalIgnoreCase);

    public static string FormatSize(long bytes)
    {
        string[] units = { "B", "KB", "MB", "GB", "TB" };
        var size = (double)bytes;
        var unit = 0;
        while (size >= 1024 && unit < units.Length - 1) { size /= 1024; unit++; }
        return unit == 0 ? $"{bytes} B" : $"{size:0.#} {units[unit]}";
    }
}
