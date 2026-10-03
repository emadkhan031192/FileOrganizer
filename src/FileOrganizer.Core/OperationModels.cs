namespace FileOrganizer.Core;

/// <summary>One logged file operation. A batch of operations sharing a BatchId is undone together (↶ Undo).</summary>
public sealed class OperationEntry
{
    public string Id { get; set; } = Guid.NewGuid().ToString("N");
    public string BatchId { get; set; } = "";
    public DateTime TimestampUtc { get; set; } = DateTime.UtcNow;
    public string Kind { get; set; } = ""; // Move | Copy | Rename
    public string SourcePath { get; set; } = "";
    public string DestinationPath { get; set; } = "";
    public bool Undone { get; set; }
}

/// <summary>The outcome of one file inside a batch operation.</summary>
public sealed class FileOperationResult
{
    public required string SourcePath { get; init; }
    public string? DestinationPath { get; init; }
    public bool Success { get; init; }
    public bool Skipped { get; init; }
    public string? Error { get; init; }

    public static FileOperationResult Ok(string src, string dst) => new() { SourcePath = src, DestinationPath = dst, Success = true };
    public static FileOperationResult Skip(string src, string reason) => new() { SourcePath = src, Success = false, Skipped = true, Error = reason };
    public static FileOperationResult Fail(string src, string error) => new() { SourcePath = src, Success = false, Error = error };
}

/// <summary>Aggregate result of a move/copy batch, including the BatchId used for Undo.</summary>
public sealed class BatchResult
{
    public string BatchId { get; init; } = Guid.NewGuid().ToString("N");
    public List<FileOperationResult> Files { get; } = new();
    public int Succeeded => Files.Count(f => f.Success);
    public int SkippedCount => Files.Count(f => f.Skipped);
    public int Failed => Files.Count(f => !f.Success && !f.Skipped);
}

/// <summary>One row of the ORGANIZATION PREVIEW (§10): where a file would go, and why.</summary>
public sealed class PreviewItem
{
    public required string SourcePath { get; init; }
    public required string FileName { get; init; }
    public required string DestinationFolder { get; init; }
    public required string DestinationPath { get; init; }

    /// <summary>Human-readable reason, e.g. "Rule: CVs to Jobs", "PDF → Documents\PDF", "Other".</summary>
    public required string Reason { get; init; }

    /// <summary>True when a different file already occupies DestinationPath (handled per ConflictPolicy at execute time).</summary>
    public bool HasConflict { get; init; }

    /// <summary>Unchecked in the preview = left out of the run. Defaults to true.</summary>
    public bool Included { get; set; } = true;
}

/// <summary>A set of files with identical size + SHA-256 content (§8).</summary>
public sealed class DuplicateGroup
{
    public required string Hash { get; init; }
    public required long SizeBytes { get; init; }
    public List<string> Files { get; } = new();

    /// <summary>Bytes reclaimable if all but one copy are removed.</summary>
    public long WastedBytes => Files.Count > 1 ? SizeBytes * (Files.Count - 1) : 0;
}

public enum DuplicateKeepStrategy
{
    KeepFirst,
    KeepNewest,
    KeepOldest,
}

public enum CaseMode
{
    None,
    Upper,
    Lower,
    TitleCase,
    CapitalizeFirstWord,
}

public enum RenameDatePosition
{
    Beginning,
    End,
}

/// <summary>All batch-rename operations (§7). Applied in this order: replace/remove text → case → prefix/suffix → date → numbering.</summary>
public sealed class RenameOptions
{
    public string ReplaceFrom { get; set; } = "";
    public string ReplaceTo { get; set; } = "";
    public string RemoveText { get; set; } = "";
    public CaseMode Case { get; set; } = CaseMode.None;
    public string Prefix { get; set; } = "";
    public string Suffix { get; set; } = "";

    public bool AddDate { get; set; }
    public RenameDatePosition DatePosition { get; set; } = RenameDatePosition.End;

    /// <summary>.NET date format string, e.g. "yyyy-MM-dd", "dd-MM-yyyy", "yyyy_MM_dd", "dd_MMM_yyyy".</summary>
    public string DateFormat { get; set; } = "yyyy-MM-dd";
    public DateSource DateSource { get; set; } = DateSource.Modified;
    public bool AddTime { get; set; }

    public bool AddNumbering { get; set; }
    public int NumberStart { get; set; } = 1;
    public int NumberPadding { get; set; } = 3;
    public string NumberSeparator { get; set; } = "_";
}

public sealed class RenamePreset
{
    public string Name { get; set; } = "";
    public RenameOptions Options { get; set; } = new();
}

/// <summary>One row of a batch-rename preview.</summary>
public sealed class RenamePreviewItem
{
    public required string SourcePath { get; init; }
    public required string OldName { get; init; }
    public required string NewName { get; init; }
    public required string NewPath { get; init; }
    public bool HasConflict { get; init; }
}

/// <summary>Filters for fast search (§9). All filters are optional and combined with AND.</summary>
public sealed class SearchQuery
{
    public string NameContains { get; set; } = "";
    public string Extension { get; set; } = ""; // without dot
    public long? MinSizeBytes { get; set; }
    public long? MaxSizeBytes { get; set; }
    public DateTime? ModifiedAfter { get; set; }
    public DateTime? ModifiedBefore { get; set; }
    public bool Recursive { get; set; } = true;
}

/// <summary>Options for smart date organization (§5), e.g. Jobs\2026\October or Jobs\2026\October\PDF.</summary>
public sealed class DateOrganizeOptions
{
    public bool Enabled { get; set; }
    public DateSource Source { get; set; } = DateSource.Modified;

    /// <summary>When true the category/extension folder is appended after Year\Month.</summary>
    public bool AppendCategoryFolder { get; set; } = true;
}
