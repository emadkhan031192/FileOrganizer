namespace FileOrganizer.Core;

/// <summary>How a file operation treats the destination folder.</summary>
public enum TransferMode
{
    Move,
    Copy,
}

/// <summary>What to do when the destination already contains a file with the same name.</summary>
public enum ConflictPolicy
{
    /// <summary>Leave the source where it is; report it as skipped.</summary>
    Skip,

    /// <summary>Save as "name (1).ext", "name (2).ext", ... — never overwrite.</summary>
    AutoRename,

    /// <summary>Overwrite the existing file. Only ever used after an explicit user confirmation in the UI.</summary>
    Replace,
}

/// <summary>Which timestamp a date-based feature reads.</summary>
public enum DateSource
{
    Created,
    Modified,
    FileName,
}

/// <summary>
/// A saved "MOVE TO" destination shown on the Quick Destination Bar.
/// Destinations form a tree via <see cref="ParentId"/> (e.g. "Afzal E Services" → "Jobs").
/// </summary>
public sealed class Destination
{
    public string Id { get; set; } = Guid.NewGuid().ToString("N");
    public string Name { get; set; } = "";

    /// <summary>Absolute folder path. May contain environment variables (e.g. %USERPROFILE%\Downloads).</summary>
    public string Path { get; set; } = "";

    /// <summary>Single emoji or short glyph shown on the button, e.g. "📁", "💼". Stored as text so it survives JSON round-trips.</summary>
    public string Icon { get; set; } = "📁";

    /// <summary>Accent colour as #RRGGBB, or empty for the default look.</summary>
    public string ColorHex { get; set; } = "";

    /// <summary>Id of the parent destination, or null for a top-level button.</summary>
    public string? ParentId { get; set; }

    /// <summary>Display order within its level (drag-and-drop reorder writes this).</summary>
    public int SortOrder { get; set; }

    public string ExpandedPath => Environment.ExpandEnvironmentVariables(Path ?? "");

    public override string ToString() => Name;
}

/// <summary>One extension → category mapping used by ⚡ ORGANIZE (e.g. PDF → Documents\PDF).</summary>
public sealed class ExtensionMapping
{
    /// <summary>Extension without the dot, lower-case (e.g. "pdf").</summary>
    public string Extension { get; set; } = "";

    /// <summary>Folder relative to the organized root, e.g. "Documents\PDF" or "Images".</summary>
    public string RelativeFolder { get; set; } = "";
}

public enum RuleField
{
    Extension,
    FileName,
    SizeBytes,
    Created,
    Modified,
}

public enum RuleOperator
{
    Equals,
    Contains,
    StartsWith,
    EndsWith,
    GreaterThan,
    LessThan,
}

/// <summary>One condition of a custom rule, e.g. extension = PDF, filename contains "CV".</summary>
public sealed class RuleCondition
{
    public RuleField Field { get; set; } = RuleField.Extension;
    public RuleOperator Operator { get; set; } = RuleOperator.Equals;
    public string Value { get; set; } = "";
}

public enum RuleLogic
{
    All, // AND
    Any, // OR
}

/// <summary>
/// A user rule: IF conditions match THEN move/copy to <see cref="TargetRelativeFolder"/>
/// (relative to the folder being organized) or to an absolute <see cref="TargetAbsolutePath"/>.
/// Rules are evaluated in <see cref="Priority"/> order (lowest first) before the extension map.
/// </summary>
public sealed class OrganizeRule
{
    public string Id { get; set; } = Guid.NewGuid().ToString("N");
    public string Name { get; set; } = "";
    public bool Enabled { get; set; } = true;
    public int Priority { get; set; } = 100;
    public RuleLogic Logic { get; set; } = RuleLogic.All;
    public List<RuleCondition> Conditions { get; set; } = new();

    /// <summary>Folder relative to the organized root, e.g. "Jobs\CV". Used when TargetAbsolutePath is empty.</summary>
    public string TargetRelativeFolder { get; set; } = "";

    /// <summary>Absolute destination folder (a Quick Destination can be targeted this way). Wins over the relative folder.</summary>
    public string TargetAbsolutePath { get; set; } = "";
}

/// <summary>User preferences persisted with the config. Everything is local; the app works fully offline.</summary>
public sealed class AppPreferences
{
    public TransferMode DefaultTransferMode { get; set; } = TransferMode.Move;
    public ConflictPolicy DefaultConflictPolicy { get; set; } = ConflictPolicy.AutoRename;

    /// <summary>Show the ORGANIZATION PREVIEW before any ⚡ ORGANIZE run (recommended; see §10 of the spec).</summary>
    public bool ConfirmBeforeOrganize { get; set; } = true;

    /// <summary>After a context-menu / Quick Bar move, show a small confirmation instead of silently closing.</summary>
    public bool NotifyAfterQuickMove { get; set; } = true;

    /// <summary>Open the floating Quick Bar together with the main window.</summary>
    public bool ShowQuickBarOnStart { get; set; } = true;

    /// <summary>Start File Organizer when Windows starts (written to HKCU Run by the installer / settings toggle).</summary>
    public bool RunAtStartup { get; set; }

    /// <summary>Include subfolders when organizing / searching a folder.</summary>
    public bool IncludeSubfolders { get; set; }

    public DateSource OrganizeDateSource { get; set; } = DateSource.Modified;

    // ----- Quick Bar customization (Settings) -----

    /// <summary>Show the single ⚡ ORGANIZE button on the Quick Bar.</summary>
    public bool ShowOrganizeOnQuickBar { get; set; } = true;

    /// <summary>Show the ✏️ Rename button on the Quick Bar.</summary>
    public bool ShowRenameOnQuickBar { get; set; } = true;

    /// <summary>Destination buttons show "icon + name" when true, icon only when false.</summary>
    public bool QuickBarShowLabels { get; set; } = true;

    /// <summary>Quick Bar button size: "S", "M", or "L".</summary>
    public string QuickBarSize { get; set; } = "M";

    /// <summary>UI theme: "Light" (default) or "Dark".</summary>
    public string Theme { get; set; } = "Light";

    /// <summary>Accent colour as #RRGGBB used for primary buttons and highlights.</summary>
    public string AccentHex { get; set; } = "#2563EB";

    /// <summary>Quick Bar / Main organize profile: "SortedDocuments" (your doc-type tree) or "Standard".</summary>
    public string OrganizeProfile { get; set; } = "SortedDocuments";

    // ----- Docking / shortcuts -----

    /// <summary>Quick Bar placement: "Free" (floating), "ExplorerTop" (inside Explorer, ribbon area), "ExplorerBottom" (inside Explorer, bottom).</summary>
    public string QuickBarDockMode { get; set; } = "Free";

    /// <summary>Global shortcuts: Explorer selection + Alt+1..Alt+9 moves to destination 1..9. App must be running.</summary>
    public bool EnableHotkeys { get; set; } = true;
}

/// <summary>Everything File Organizer persists locally (destinations, rules, categories, presets, recent folders).</summary>
public sealed class AppConfig
{
    public int SchemaVersion { get; set; } = 1;
    public List<Destination> Destinations { get; set; } = new();
    public List<ExtensionMapping> ExtensionMap { get; set; } = new();
    public List<OrganizeRule> Rules { get; set; } = new();
    public List<RenamePreset> RenamePresets { get; set; } = new();
    public List<string> RecentFolders { get; set; } = new();
    public AppPreferences Preferences { get; set; } = new();

    /// <summary>
    /// First-run config: the example destinations from the spec, pointing under the user's
    /// Documents folder so the buttons work immediately, plus the default extension map.
    /// No folder is created until the user actually moves/organizes something into it.
    /// </summary>
    public static AppConfig CreateDefault()
    {
        var docs = Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments);
        string D(string name) => System.IO.Path.Combine(docs, "Afzal E Services", name);

        var cfg = new AppConfig();
        var root = new Destination { Name = "Afzal E Services", Path = System.IO.Path.Combine(docs, "Afzal E Services"), Icon = "📁", SortOrder = 0 };
        cfg.Destinations.Add(root);
        cfg.Destinations.Add(new Destination { Name = "Jobs", Path = D("Jobs"), Icon = "💼", ParentId = root.Id, SortOrder = 1 });
        cfg.Destinations.Add(new Destination { Name = "Advertisements", Path = D("Advertisements"), Icon = "📢", ParentId = root.Id, SortOrder = 2 });
        cfg.Destinations.Add(new Destination { Name = "School", Path = D("School"), Icon = "🏫", ParentId = root.Id, SortOrder = 3 });
        cfg.Destinations.Add(new Destination { Name = "Documents", Path = System.IO.Path.Combine(docs, "Documents"), Icon = "📄", SortOrder = 4 });
        cfg.Destinations.Add(new Destination { Name = "Personal", Path = System.IO.Path.Combine(docs, "Personal"), Icon = "🔒", SortOrder = 5 });

        foreach (var (ext, folder) in DefaultExtensionMap())
            cfg.ExtensionMap.Add(new ExtensionMapping { Extension = ext, RelativeFolder = folder });

        cfg.Rules.Add(new OrganizeRule
        {
            Name = "CVs to Jobs",
            Priority = 10,
            Conditions = { new RuleCondition { Field = RuleField.FileName, Operator = RuleOperator.Contains, Value = "CV" } },
            TargetRelativeFolder = @"Jobs\CV",
        });

        cfg.RenamePresets.AddRange(new[]
        {
            new RenamePreset { Name = "Capitalize Words", Options = new RenameOptions { Case = CaseMode.TitleCase } },
            new RenamePreset { Name = "Make Web-safe", Options = new RenameOptions { MakeWebSafe = true } },
            new RenamePreset { Name = "Number Files", Options = new RenameOptions { AddNumbering = true, NumberStart = 1, NumberPadding = 3, NumberSeparator = "_" } },
            new RenamePreset { Name = "Time-Stamp Names", Options = new RenameOptions { AddDate = true, DatePosition = RenameDatePosition.Beginning, DateFormat = "yyyy-MM-dd", AddTime = true, DateSource = DateSource.Modified } },
            new RenamePreset { Name = "Underscores to Spaces", Options = new RenameOptions { ReplaceFrom = "_", ReplaceTo = " " } },
            new RenamePreset { Name = "Unique Number", Options = new RenameOptions { AddNumbering = true, NumberStart = 1, NumberPadding = 4, NumberSeparator = "-" } },
        });
        return cfg;
    }

    /// <summary>
    /// "Sorted Documents" profile: document files go into a `Sorted Documents` tree using the
    /// folder names from the user's own sorted library (Sorted Pdfs, Sorted Word Files, PSDs, …).
    /// Non-document files keep the standard top-level categories.
    /// </summary>
    public static IEnumerable<ExtensionMapping> SortedDocumentsMap()
    {
        // Overrides first: OrganizeEngine keeps the first mapping per extension.
        (string Folder, string[] Exts)[] groups =
        {
            (@"Sorted Documents\Sorted Pdfs", new[] { "pdf" }),
            (@"Sorted Documents\Sorted Word Files", new[] { "doc", "docx", "odt" }),
            (@"Sorted Documents\Sorted Excel Files", new[] { "xls", "xlsx", "xlsm", "csv" }),
            (@"Sorted Documents\Sorted Powerpoint files", new[] { "ppt", "pptx", "pps", "ppsx", "odp" }),
            (@"Sorted Documents\Sorted Text", new[] { "txt", "md", "log", "rtf" }),
            (@"Sorted Documents\PSDs", new[] { "psd", "psb" }),
            (@"Sorted Documents\Illustrator Files", new[] { "ai", "eps" }),
            (@"Sorted Documents\Html Files", new[] { "html", "htm", "xhtml" }),
            (@"Sorted Documents\Affinity designer", new[] { "afphoto", "afdesign", "afpub", "af" }),
        };
        foreach (var (folder, exts) in groups)
            foreach (var ext in exts)
                yield return new ExtensionMapping { Extension = ext, RelativeFolder = folder };

        // Everything else keeps the standard categories (Images, Videos, Audio, Archives, …).
        foreach (var m in DefaultExtensionMap().Select(t => new ExtensionMapping { Extension = t.Ext, RelativeFolder = t.Folder }))
            yield return m;
    }

    /// <summary>The default ⚡ ORGANIZE categories from the spec (§3).</summary>
    public static IEnumerable<(string Ext, string Folder)> DefaultExtensionMap()
    {
        foreach (var e in new[] { "jpg", "jpeg", "png", "webp", "gif", "bmp", "svg" }) yield return (e, @"Images");
        foreach (var e in new[] { "pdf" }) yield return (e, @"Documents\PDF");
        foreach (var e in new[] { "doc", "docx" }) yield return (e, @"Documents\Word");
        foreach (var e in new[] { "xls", "xlsx", "csv" }) yield return (e, @"Documents\Excel");
        foreach (var e in new[] { "ppt", "pptx" }) yield return (e, @"Documents\Presentations");
        foreach (var e in new[] { "txt", "md", "rtf" }) yield return (e, @"Documents\Text");
        foreach (var e in new[] { "mp4", "mkv", "avi", "mov", "webm" }) yield return (e, @"Videos");
        foreach (var e in new[] { "mp3", "wav", "flac", "ogg", "m4a" }) yield return (e, @"Audio");
        foreach (var e in new[] { "zip", "rar", "7z", "tar", "gz" }) yield return (e, @"Archives");
    }
}
