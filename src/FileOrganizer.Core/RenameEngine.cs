using System.Globalization;

namespace FileOrganizer.Core;

/// <summary>Batch rename (§7): date stamping, numbering, text operations. Always preview first; never overwrites. Works on files AND folders.</summary>
public static class RenameEngine
{
    public static List<RenamePreviewItem> BuildPreview(IReadOnlyList<string> files, RenameOptions options)
    {
        var items = new List<RenamePreviewItem>();
        for (var i = 0; i < files.Count; i++)
        {
            var source = files[i].TrimEnd('\\', '/');
            var isDirectory = Directory.Exists(source);
            var file = new FileInfo(source);
            // Folders: the whole name is the "stem" (a dot in a folder name is not an extension).
            var stem = isDirectory ? file.Name : Path.GetFileNameWithoutExtension(file.Name);
            var ext = isDirectory ? "" : file.Extension; // includes dot

            if (!string.IsNullOrEmpty(options.RemoveText))
                stem = stem.Replace(options.RemoveText, "", StringComparison.OrdinalIgnoreCase);
            if (!string.IsNullOrEmpty(options.ReplaceFrom))
                stem = stem.Replace(options.ReplaceFrom, options.ReplaceTo ?? "", StringComparison.Ordinal);

            stem = ApplyCase(stem, options.Case);

            if (options.MakeWebSafe)
                stem = ToWebSafe(stem);

            if (!string.IsNullOrEmpty(options.Prefix))
                stem = options.Prefix + stem;
            if (!string.IsNullOrEmpty(options.Suffix))
                stem = stem + options.Suffix;

            if (options.AddDate)
            {
                var date = RulesEngine.GetFileDate(file, options.DateSource);
                var stamp = date.ToString(SanitizeFormat(options), CultureInfo.InvariantCulture);
                stem = options.DatePosition == RenameDatePosition.Beginning
                    ? $"{stamp}_{stem}"
                    : $"{stem}_{stamp}";
            }

            if (options.AddNumbering)
            {
                var number = (options.NumberStart + i).ToString($"D{Math.Max(1, options.NumberPadding)}", CultureInfo.InvariantCulture);
                stem = $"{stem}{options.NumberSeparator}{number}";
            }

            var newName = stem + ext;
            var newPath = Path.Combine(file.DirectoryName ?? "", newName);
            items.Add(new RenamePreviewItem
            {
                SourcePath = source,
                OldName = file.Name,
                NewName = newName,
                NewPath = newPath,
                HasConflict = PathHelpers.PathExists(newPath) && !PathHelpers.IsSamePath(source, newPath),
            });
        }
        return items;
    }

    private static string SanitizeFormat(RenameOptions o)
    {
        var format = string.IsNullOrWhiteSpace(o.DateFormat) ? "yyyy-MM-dd" : o.DateFormat;
        return o.AddTime ? format + " HH-mm" : format;
    }

    private static string ApplyCase(string stem, CaseMode mode) => mode switch
    {
        CaseMode.Upper => stem.ToUpperInvariant(),
        CaseMode.Lower => stem.ToLowerInvariant(),
        CaseMode.TitleCase => CultureInfo.InvariantCulture.TextInfo.ToTitleCase(stem.ToLowerInvariant()),
        CaseMode.CapitalizeFirstWord => stem.Length == 0 ? stem : char.ToUpperInvariant(stem[0]) + stem[1..],
        _ => stem,
    };

    private static string ToWebSafe(string stem)
    {
        var lowered = stem.ToLowerInvariant().Replace(' ', '-').Replace('_', '-');
        var chars = lowered.Where(c => (c >= 'a' && c <= 'z') || (c >= '0' && c <= '9') || c == '-').ToArray();
        var collapsed = new string(chars);
        while (collapsed.Contains("--"))
            collapsed = collapsed.Replace("--", "-");
        return collapsed.Trim('-');
    }

    /// <summary>Applies a confirmed rename preview. Collisions get " (1)" suffixes; every rename is logged for Undo.</summary>
    public static BatchResult Apply(IReadOnlyList<RenamePreviewItem> preview, HistoryService history)
    {
        var result = new BatchResult();
        var logged = new List<(string, string)>();
        foreach (var item in preview)
        {
            try
            {
                var isDirectory = Directory.Exists(item.SourcePath);
                if (!isDirectory && !File.Exists(item.SourcePath))
                {
                    result.Files.Add(FileOperationResult.Fail(item.SourcePath, "File or folder no longer exists."));
                    continue;
                }
                if (string.Equals(item.SourcePath, item.NewPath, StringComparison.Ordinal))
                {
                    result.Files.Add(FileOperationResult.Ok(item.SourcePath, item.NewPath));
                    continue;
                }
                var target = PathHelpers.PathExists(item.NewPath) && !PathHelpers.IsSamePathIgnoreCase(item.SourcePath, item.NewPath)
                    ? PathHelpers.GetUniqueFilePath(item.NewPath)
                    : item.NewPath;
                if (PathHelpers.IsSamePathIgnoreCase(item.SourcePath, item.NewPath))
                {
                    // Case-only rename (e.g. "photo.jpg" → "Photo.jpg"): route via a temp name so the
                    // displayed case actually changes on case-insensitive filesystems (Windows/NTFS).
                    var temp = item.NewPath + ".fileorganizer-tmp";
                    MoveItem(item.SourcePath, temp, isDirectory);
                    MoveItem(temp, target, isDirectory);
                }
                else
                {
                    MoveItem(item.SourcePath, target, isDirectory);
                }
                result.Files.Add(FileOperationResult.Ok(item.SourcePath, target));
                logged.Add((item.SourcePath, target));
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                result.Files.Add(FileOperationResult.Fail(item.SourcePath, ex.Message));
            }
        }
        if (logged.Count > 0)
            history.RecordBatch(result.BatchId, "Rename", logged);
        return result;
    }

    private static void MoveItem(string source, string target, bool isDirectory)
    {
        if (isDirectory)
            Directory.Move(source, target);
        else
            File.Move(source, target);
    }
}
