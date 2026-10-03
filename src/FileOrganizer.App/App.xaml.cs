using System.IO;
using System.Windows;
using FileOrganizer.Core;

namespace FileOrganizer.App;

/// <summary>
/// Entry point. Two modes:
///  GUI (no args): Main Window + optional floating Quick Bar.
///  Headless CLI (used by the Explorer context menu / Send To shortcuts):
///    FileOrganizer.exe --move-to &lt;destinationIdOrName&gt; &lt;file1&gt; [file2 ...]
///    FileOrganizer.exe --add-destination &lt;folder&gt;
///    FileOrganizer.exe --organize &lt;folder&gt;
///    FileOrganizer.exe --quickbar
/// </summary>
public partial class App : Application
{
    public static AppState State { get; private set; } = null!;

    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);
        State = new AppState();

        var args = e.Args;
        if (args.Length > 0 && HandleCommandLine(args))
        {
            Shutdown(); // a headless command ran; do not open the GUI
            return;
        }
        // No args (or unrecognised args): open the GUI.

        var main = new MainWindow();
        MainWindow = main;
        main.Show();

        if (State.Config.Preferences.ShowQuickBarOnStart)
            QuickBarWindow.ShowBar();
    }

    /// <summary>Returns true when a headless command was handled (caller should exit).</summary>
    private bool HandleCommandLine(string[] args)
    {
        try
        {
            switch (args[0].ToLowerInvariant())
            {
                case "--move-to" when args.Length >= 3:
                {
                    var dest = FindDestination(args[1]);
                    if (dest is null)
                    {
                        MessageBox.Show($"Destination not found: {args[1]}", "File Organizer", MessageBoxButton.OK, MessageBoxImage.Warning);
                        return true;
                    }
                    var files = args.Skip(2).Where(File.Exists).ToList();
                    var result = State.Ops.TransferFiles(files, dest.ExpandedPath,
                        State.Config.Preferences.DefaultTransferMode,
                        State.Config.Preferences.DefaultConflictPolicy);
                    if (State.Config.Preferences.NotifyAfterQuickMove)
                        MessageBox.Show(
                            $"Moved {result.Succeeded} file(s) to {dest.Name}." +
                            (result.SkippedCount > 0 ? $"\nSkipped {result.SkippedCount} (already existed)." : "") +
                            (result.Failed > 0 ? $"\nFailed {result.Failed}." : ""),
                            "File Organizer", MessageBoxButton.OK,
                            result.Failed > 0 ? MessageBoxImage.Warning : MessageBoxImage.Information);
                    return true;
                }
                case "--add-destination" when args.Length >= 2 && Directory.Exists(args[1]):
                {
                    State.Config.Destinations.Add(new Destination
                    {
                        Name = Path.GetFileName(args[1].TrimEnd('\\', '/')),
                        Path = args[1],
                        Icon = "📁",
                        SortOrder = State.Config.Destinations.Count,
                    });
                    State.Save();
                    MessageBox.Show($"Added destination: {args[1]}", "File Organizer");
                    return true;
                }
                case "--organize" when args.Length >= 2 && Directory.Exists(args[1]):
                {
                    var engine = new OrganizeEngine(State.Config);
                    var preview = engine.BuildPreview(args[1], State.Config.Preferences.IncludeSubfolders);
                    var applied = State.Ops.ApplyPairs(
                        preview.Select(p => (p.SourcePath, p.DestinationPath)), TransferMode.Move,
                        State.Config.Preferences.DefaultConflictPolicy);
                    MessageBox.Show($"Organized {applied.Succeeded} file(s) in {args[1]}.", "File Organizer");
                    return true;
                }
                default:
                    return false; // unknown args → fall through to the GUI
            }
        }
        catch (Exception ex)
        {
            MessageBox.Show(ex.Message, "File Organizer", MessageBoxButton.OK, MessageBoxImage.Error);
            return true;
        }
    }

    private Destination? FindDestination(string idOrName) =>
        State.Config.Destinations.FirstOrDefault(d => d.Id == idOrName)
        ?? State.Config.Destinations.FirstOrDefault(d =>
            string.Equals(d.Name, idOrName, StringComparison.OrdinalIgnoreCase));
}
