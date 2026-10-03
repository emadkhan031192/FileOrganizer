using System.Globalization;

namespace FileOrganizer.Core;

/// <summary>
/// ⚡ ORGANIZE (§3/§5/§10): builds a PREVIEW of where every file in a folder would go —
/// custom rules first (lowest Priority number wins), then the extension map, then "Other".
/// Nothing is moved until the caller executes the confirmed preview via
/// <see cref="FileOperationService.ApplyPairs"/>.
/// </summary>
public sealed class OrganizeEngine
{
    private readonly AppConfig _config;
    private readonly List<ExtensionMapping> _mapOverride;

    public OrganizeEngine(AppConfig config, IEnumerable<ExtensionMapping>? mapOverride = null)
    {
        _config = config;
        _mapOverride = mapOverride?.ToList() ?? config.ExtensionMap;
    }

    public List<PreviewItem> BuildPreview(
        string rootFolder,
        bool recursive = false,
        DateOrganizeOptions? dateOptions = null)
    {
        var root = Path.GetFullPath(Environment.ExpandEnvironmentVariables(rootFolder));
        if (!Directory.Exists(root))
            throw new DirectoryNotFoundException($"Folder not found: {root}");

        var map = _mapOverride
            .GroupBy(m => m.Extension.TrimStart('.'), StringComparer.OrdinalIgnoreCase)
            .ToDictionary(g => g.Key, g => g.First().RelativeFolder, StringComparer.OrdinalIgnoreCase);
        var rules = _config.Rules.Where(r => r.Enabled).OrderBy(r => r.Priority).ToList();
        var date = dateOptions ?? new DateOrganizeOptions();

        var option = recursive ? SearchOption.AllDirectories : SearchOption.TopDirectoryOnly;
        var items = new List<PreviewItem>();

        foreach (var path in Directory.EnumerateFiles(root, "*", option))
        {
            var file = new FileInfo(path);
            // Skip files already sitting in their target folder (re-running ORGANIZE is a no-op for them).
            var (targetFolder, reason) = Classify(file, root, map, rules);

            string destinationFolder;
            if (date.Enabled && !Path.IsPathFullyQualified(targetFolder))
            {
                var d = RulesEngine.GetFileDate(file, date.Source);
                var datePart = Path.Combine(d.ToString("yyyy", CultureInfo.InvariantCulture),
                                            d.ToString("MMMM", CultureInfo.InvariantCulture));
                destinationFolder = date.AppendCategoryFolder
                    ? PathHelpers.Combine(root, datePart, targetFolder)
                    : PathHelpers.Combine(root, datePart);
                reason += $" · by {date.Source.ToString().ToLowerInvariant()} date";
            }
            else
            {
                destinationFolder = Path.IsPathFullyQualified(targetFolder)
                    ? targetFolder
                    : PathHelpers.Combine(root, targetFolder);
            }

            var destinationPath = Path.Combine(destinationFolder, file.Name);
            if (PathHelpers.IsSamePath(path, destinationPath))
                continue;

            items.Add(new PreviewItem
            {
                SourcePath = path,
                FileName = file.Name,
                DestinationFolder = destinationFolder,
                DestinationPath = destinationPath,
                Reason = reason,
                HasConflict = File.Exists(destinationPath),
            });
        }

        return items.OrderBy(i => i.DestinationPath, StringComparer.OrdinalIgnoreCase).ToList();
    }

    private static (string RelativeFolder, string Reason) Classify(
        FileInfo file, string root, Dictionary<string, string> map, List<OrganizeRule> rules)
    {
        foreach (var rule in rules)
        {
            if (!RulesEngine.Matches(rule, file))
                continue;
            var target = !string.IsNullOrWhiteSpace(rule.TargetAbsolutePath)
                ? rule.TargetAbsolutePath
                : rule.TargetRelativeFolder;
            // Absolute rule targets are returned as-is; the preview builder roots relative ones at 'root'.
            return (target, $"Rule: {rule.Name}");
        }

        var ext = file.Extension.TrimStart('.');
        if (map.TryGetValue(ext, out var folder))
            return (folder, $"{ext.ToUpperInvariant()} → {folder}");

        return ("Other", "Other");
    }
}
