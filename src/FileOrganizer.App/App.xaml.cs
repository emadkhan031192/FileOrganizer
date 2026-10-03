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
        ApplyTheme(State.Config.Preferences);

        var args = e.Args;
        if (args.Length >= 2 &&
            string.Equals(args[0], "--organize", StringComparison.OrdinalIgnoreCase) &&
            System.IO.Directory.Exists(args[1]))
        {
            // Interactive right-click flow: preview → progress → done, without opening the main window.
            ShutdownMode = ShutdownMode.OnExplicitShutdown;
            _ = OrganizeFromCommandLineAsync(args[1]);
            return;
        }
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

    /// <summary>
    /// "Organize this folder" (Explorer right-click): builds the preview with the active profile,
    /// asks for confirmation (unless disabled in Settings), then organizes with progress + Cancel.
    /// </summary>
    private async Task OrganizeFromCommandLineAsync(string folder)
    {
        try
        {
            var preview = await Task.Run(() => QuickBarWindow.CreateEngine().BuildPreview(
                folder, State.Config.Preferences.IncludeSubfolders));
            if (preview.Count == 0)
            {
                MessageBox.Show($"Nothing to organize in:\n{folder}", "File Organizer",
                    MessageBoxButton.OK, MessageBoxImage.Information);
                return;
            }

            var confirmed = preview;
            if (State.Config.Preferences.ConfirmBeforeOrganize)
            {
                var window = new PreviewWindow(preview);
                if (window.ShowDialog() != true)
                    return;
                confirmed = window.ConfirmedItems;
                if (confirmed.Count == 0)
                    return;
            }

            var host = new Window { Width = 1, Height = 1, ShowInTaskbar = false, WindowStyle = WindowStyle.None, Opacity = 0 };
            host.Show();
            var pairs = confirmed.Select(p => (p.SourcePath, p.DestinationPath)).ToList();
            var result = await TransferHelper.RunApplyWithProgressAsync(host, pairs,
                State.Config.Preferences.DefaultTransferMode,
                State.Config.Preferences.DefaultConflictPolicy);
            host.Close();
            MessageBox.Show(result.Cancelled
                    ? $"Cancelled — {result.Succeeded} file(s) were already organized in {folder}.\n(↶ Undo in File Organizer reverses them.)"
                    : $"Organized {result.Succeeded} file(s) in {folder}." +
                      (result.Failed > 0 ? $"\nFailed: {result.Failed}" : ""),
                "File Organizer", MessageBoxButton.OK, MessageBoxImage.Information);
        }
        catch (Exception ex)
        {
            MessageBox.Show(ex.Message, "File Organizer", MessageBoxButton.OK, MessageBoxImage.Error);
        }
        finally
        {
            Shutdown();
        }
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
                    var files = args.Skip(2).Where(p => File.Exists(p) || Directory.Exists(p)).ToList();
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

    /// <summary>Applies Light/Dark theme + accent colour by swapping the DynamicResource brushes.</summary>
    public static void ApplyTheme(Core.AppPreferences prefs)
    {
        void Set(string key, string hex)
        {
            if (System.Windows.Media.ColorConverter.ConvertFromString(hex) is System.Windows.Media.Color c)
                Current.Resources[key] = new System.Windows.Media.SolidColorBrush(c);
        }

        if (string.Equals(prefs.Theme, "Dark", StringComparison.OrdinalIgnoreCase))
        {
            Set("WindowBg", "#171A21");
            Set("CardBg", "#21252E");
            Set("CardBorder", "#3A4152");
            Set("TextPrimary", "#E9EDF5");
            Set("TextSecondary", "#9AA7C2");
            Set("InputBg", "#262B36");
            Set("AccentSoft", "#2B3550");
        }
        else
        {
            Set("WindowBg", "#F3F5FA");
            Set("CardBg", "#FFFFFF");
            Set("CardBorder", "#D9E1F2");
            Set("TextPrimary", "#17203A");
            Set("TextSecondary", "#5A6B8C");
            Set("InputBg", "#FFFFFF");
            Set("AccentSoft", "#E8EEFE");
        }

        if (!string.IsNullOrWhiteSpace(prefs.AccentHex))
        {
            Set("Accent", prefs.AccentHex);
            Set("AccentHover", prefs.AccentHex);
        }
    }

    private Destination? FindDestination(string idOrName) =>
        State.Config.Destinations.FirstOrDefault(d => d.Id == idOrName)
        ?? State.Config.Destinations.FirstOrDefault(d =>
            string.Equals(d.Name, idOrName, StringComparison.OrdinalIgnoreCase));
}
