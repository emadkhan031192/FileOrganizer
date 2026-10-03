namespace FileOrganizer.Core;

/// <summary>
/// The one place that actually touches files for move/copy/rename (§15 Safety).
/// Guarantees: destination folder is created if missing, a file is NEVER silently
/// overwritten (see <see cref="ConflictPolicy"/>), same-path moves are no-ops,
/// cross-volume moves fall back to copy+delete, and every success is logged for Undo.
/// </summary>
public sealed class FileOperationService
{
    private readonly HistoryService _history;

    public FileOperationService(HistoryService history) => _history = history;

    /// <summary>Moves (or copies) files into a destination folder — the "select file → click destination" core action.</summary>
    public BatchResult TransferFiles(
        IEnumerable<string> sourcePaths,
        string destinationFolder,
        TransferMode mode,
        ConflictPolicy onConflict = ConflictPolicy.AutoRename,
        IProgress<(int Done, int Total, string CurrentFile)>? progress = null,
        CancellationToken cancellationToken = default)
    {
        var sources = sourcePaths.Where(p => !string.IsNullOrWhiteSpace(p)).Distinct(StringComparer.OrdinalIgnoreCase).ToList();
        var result = new BatchResult();
        Directory.CreateDirectory(Environment.ExpandEnvironmentVariables(destinationFolder));

        var logged = new List<(string, string)>();
        try
        {
            for (var i = 0; i < sources.Count; i++)
            {
                cancellationToken.ThrowIfCancellationRequested();
                var source = sources[i];
                progress?.Report((i, sources.Count, Path.GetFileName(source)));
                var r = TransferOne(source, destinationFolder, mode, onConflict);
                result.Files.Add(r);
                if (r.Success && r.DestinationPath is not null)
                    logged.Add((source, r.DestinationPath));
            }
            progress?.Report((sources.Count, sources.Count, ""));
        }
        catch (OperationCanceledException)
        {
            result.Cancelled = true; // items already moved stay moved — and are logged below for Undo
        }

        if (logged.Count > 0)
            _history.RecordBatch(result.BatchId, mode.ToString(), logged);
        return result;
    }

    private static FileOperationResult TransferOne(string source, string destinationFolder, TransferMode mode, ConflictPolicy onConflict)
    {
        try
        {
            var isDirectory = Directory.Exists(source);
            if (!isDirectory && !File.Exists(source))
                return FileOperationResult.Fail(source, "File or folder no longer exists.");

            var desired = Path.Combine(Environment.ExpandEnvironmentVariables(destinationFolder), Path.GetFileName(source.TrimEnd('\\', '/')));
            if (PathHelpers.IsSamePath(source, desired))
                return FileOperationResult.Ok(source, desired); // already there — nothing to do

            var target = desired;
            if (PathHelpers.PathExists(desired))
            {
                switch (onConflict)
                {
                    case ConflictPolicy.Skip:
                        return FileOperationResult.Skip(source, $"\"{Path.GetFileName(source)}\" already exists in the destination.");
                    case ConflictPolicy.AutoRename:
                        target = PathHelpers.GetUniqueFilePath(desired);
                        break;
                    case ConflictPolicy.Replace:
                        break; // handled below (file: delete-then-move; folder: replace-whole-folder, UI confirms first)
                }
            }

            if (isDirectory)
                TransferDirectory(source, target, mode, onConflict);
            else if (mode == TransferMode.Copy)
                File.Copy(source, target, overwrite: onConflict == ConflictPolicy.Replace);
            else
            {
                try
                {
                    if (onConflict == ConflictPolicy.Replace && File.Exists(target))
                        File.Delete(target);
                    File.Move(source, target);
                }
                catch (IOException) when (!IsSameVolume(source, target))
                {
                    // File.Move across volumes can fail on some filesystems; copy + delete is the safe fallback.
                    File.Copy(source, target, overwrite: true);
                    File.Delete(source);
                }
            }
            return FileOperationResult.Ok(source, target);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return FileOperationResult.Fail(source, ex.Message);
        }
    }

    /// <summary>Moves or copies a whole folder (recursively). Cross-volume moves fall back to copy + delete.</summary>
    private static void TransferDirectory(string source, string target, TransferMode mode, ConflictPolicy onConflict)
    {
        if (onConflict == ConflictPolicy.Replace && Directory.Exists(target))
            Directory.Delete(target, recursive: true);
        else if (onConflict == ConflictPolicy.Replace && File.Exists(target))
            File.Delete(target);

        if (mode == TransferMode.Copy)
        {
            CopyDirectory(source, target);
            return;
        }

        try
        {
            Directory.Move(source, target);
        }
        catch (IOException) when (!IsSameVolume(source, target))
        {
            CopyDirectory(source, target);
            Directory.Delete(source, recursive: true);
        }
    }

    private static void CopyDirectory(string source, string target)
    {
        Directory.CreateDirectory(target);
        foreach (var file in Directory.EnumerateFiles(source))
            File.Copy(file, Path.Combine(target, Path.GetFileName(file)), overwrite: true);
        foreach (var dir in Directory.EnumerateDirectories(source))
            CopyDirectory(dir, Path.Combine(target, Path.GetFileName(dir)));
    }

    private static bool IsSameVolume(string a, string b)
    {
        var ra = Path.GetPathRoot(Path.GetFullPath(a));
        var rb = Path.GetPathRoot(Path.GetFullPath(b));
        return string.Equals(ra, rb, StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>Renames/moves arbitrary (source → destination) pairs — files or folders — e.g. applying a confirmed organization preview.</summary>
    public BatchResult ApplyPairs(
        IEnumerable<(string Source, string Destination)> pairs,
        TransferMode mode,
        ConflictPolicy onConflict = ConflictPolicy.AutoRename,
        IProgress<(int Done, int Total, string CurrentFile)>? progress = null,
        CancellationToken cancellationToken = default)
    {
        var list = pairs.ToList();
        var result = new BatchResult();
        var logged = new List<(string, string)>();
        try
        {
            for (var i = 0; i < list.Count; i++)
            {
                cancellationToken.ThrowIfCancellationRequested();
                var (source, desired) = list[i];
                progress?.Report((i, list.Count, Path.GetFileName(source)));
                FileOperationResult r;
                var folder = Path.GetDirectoryName(desired);
                if (string.IsNullOrEmpty(folder))
                {
                    r = FileOperationResult.Fail(source, "Destination has no folder.");
                }
                else
                {
                    Directory.CreateDirectory(folder);
                    r = TransferOne(source, folder, mode, onConflict) is var moved && moved.Success && moved.DestinationPath is not null
                        // TransferOne targets folder+filename; honour an explicitly different destination file name:
                        ? EnsureExactName(moved, desired, mode)
                        : moved;
                }
                result.Files.Add(r);
                if (r.Success && r.DestinationPath is not null)
                    logged.Add((source, r.DestinationPath));
            }
            progress?.Report((list.Count, list.Count, ""));
        }
        catch (OperationCanceledException)
        {
            result.Cancelled = true;
        }

        if (logged.Count > 0)
            _history.RecordBatch(result.BatchId, mode.ToString(), logged);
        return result;
    }

    private static FileOperationResult EnsureExactName(FileOperationResult moved, string desired, TransferMode mode)
    {
        // TransferOne already produced desired's name unless AutoRename kicked in;
        // if the caller asked for a different name (rename case), move within the folder.
        if (PathHelpers.IsSamePath(moved.DestinationPath!, desired) || !PathHelpers.PathExists(moved.DestinationPath!))
            return moved;
        if (mode == TransferMode.Copy)
            return moved; // copies keep the auto-renamed name; renaming a copy is the Rename engine's job
        var final = PathHelpers.PathExists(desired) ? PathHelpers.GetUniqueFilePath(desired) : desired;
        if (Directory.Exists(moved.DestinationPath))
            Directory.Move(moved.DestinationPath!, final);
        else
            File.Move(moved.DestinationPath!, final);
        return FileOperationResult.Ok(moved.SourcePath, final);
    }

    /// <summary>↶ Undo: reverses the newest not-yet-undone batch (newest file first). Copies are undone by deleting the copy.</summary>
    public BatchResult UndoLastBatch()
    {
        var batchId = _history.LastUndoableBatchId
                      ?? throw new InvalidOperationException("There is nothing to undo.");
        return UndoBatch(batchId);
    }

    public BatchResult UndoBatch(string batchId)
    {
        var entries = _history.EntriesForBatch(batchId);
        var result = new BatchResult();
        var undoneIds = new List<string>();

        foreach (var entry in entries)
        {
            try
            {
                if (entry.Kind == nameof(TransferMode.Copy) || entry.Kind == "Copy")
                {
                    // Undoing a copy deletes the copy — file or whole folder.
                    if (Directory.Exists(entry.DestinationPath))
                        Directory.Delete(entry.DestinationPath, recursive: true);
                    else if (File.Exists(entry.DestinationPath))
                        File.Delete(entry.DestinationPath);
                    result.Files.Add(FileOperationResult.Ok(entry.DestinationPath, entry.SourcePath));
                }
                else
                {
                    if (!PathHelpers.PathExists(entry.DestinationPath))
                    {
                        result.Files.Add(FileOperationResult.Fail(entry.DestinationPath, "File or folder is no longer at the moved location."));
                        continue;
                    }
                    var back = entry.SourcePath;
                    Directory.CreateDirectory(Path.GetDirectoryName(back)!);
                    if (PathHelpers.PathExists(back))
                        back = PathHelpers.GetUniqueFilePath(back); // never overwrite whatever is there now
                    if (Directory.Exists(entry.DestinationPath))
                        Directory.Move(entry.DestinationPath, back);
                    else
                        File.Move(entry.DestinationPath, back);
                    result.Files.Add(FileOperationResult.Ok(entry.DestinationPath, back));
                }
                undoneIds.Add(entry.Id);
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                result.Files.Add(FileOperationResult.Fail(entry.DestinationPath, ex.Message));
            }
        }

        _history.MarkUndone(undoneIds);
        return result;
    }
}
