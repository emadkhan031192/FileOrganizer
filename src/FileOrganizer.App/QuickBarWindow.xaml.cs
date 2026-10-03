using System.IO;
using System.Runtime.InteropServices;
using System.Text;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Threading;
using FileOrganizer.Core;

namespace FileOrganizer.App;

public partial class QuickBarWindow : Window
{
    private static QuickBarWindow? _instance;

    private AppState State => App.State;
    private readonly DispatcherTimer _dockTimer;

    private QuickBarWindow()
    {
        InitializeComponent();
        ApplyBarPreferences();
        RefreshButtons();
        // Bottom-centre by default, just above the taskbar — close to Explorer without covering it.
        Loaded += (_, _) =>
        {
            if (State.Config.Preferences.QuickBarDockMode == "Free")
            {
                Left = (SystemParameters.WorkArea.Width - ActualWidth) / 2;
                Top = SystemParameters.WorkArea.Bottom - ActualHeight - 12;
            }
        };

        // Dock mode: follow the foreground Explorer window (see FollowExplorer).
        _dockTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(350) };
        _dockTimer.Tick += (_, _) => FollowExplorer();
        _dockTimer.Start();
        Closed += (_, _) => _dockTimer.Stop();
    }

    /// <summary>
    /// Docking (feature 1): in ExplorerTop/ExplorerBottom mode the bar sticks inside the
    /// foreground Explorer window — top mode sits in the ribbon area Explorer leaves empty,
    /// bottom mode sits just above Explorer's status bar. Free mode never moves the bar.
    /// The bar never takes activation on its own, so Explorer keeps the focus/selection.
    /// </summary>
    private void FollowExplorer()
    {
        var mode = State.Config.Preferences.QuickBarDockMode;
        if (mode == "Free" || !IsVisible)
            return;

        var hwnd = GetForegroundWindow();
        if (hwnd == IntPtr.Zero)
            return;
        var className = new StringBuilder(256);
        if (GetClassName(hwnd, className, className.Capacity) == 0)
            return;
        var cls = className.ToString();
        if (cls is not ("CabinetWClass" or "ExplorerWClass")) // Explorer folder windows only
            return;
        if (!GetWindowRect(hwnd, out var rect))
            return;

        var width = ActualWidth;
        var height = ActualHeight;
        if (width <= 0 || height <= 0)
            return;

        double targetLeft, targetTop;
        if (mode == "ExplorerTop")
        {
            // Ribbon area: right-aligned under the title bar, where Explorer's ribbon is usually empty.
            targetLeft = rect.Right - width - 18;
            targetTop = rect.Top + 54;
        }
        else // ExplorerBottom
        {
            targetLeft = rect.Left + (rect.Right - rect.Left - width) / 2;
            targetTop = rect.Bottom - height - 34;
        }

        if (Math.Abs(Left - targetLeft) > 2) Left = targetLeft;
        if (Math.Abs(Top - targetTop) > 2) Top = targetTop;
    }

    [DllImport("user32.dll")]
    private static extern IntPtr GetForegroundWindow();

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    private static extern int GetClassName(IntPtr hWnd, StringBuilder lpClassName, int nMaxCount);

    [DllImport("user32.dll")]
    private static extern bool GetWindowRect(IntPtr hWnd, out Rect rect);

    [StructLayout(LayoutKind.Sequential)]
    private struct Rect
    {
        public int Left, Top, Right, Bottom;
    }

    /// <summary>Shows the single floating Quick Bar, creating it on first use.</summary>
    public static void ShowBar()
    {
        _instance ??= new QuickBarWindow();
        _instance.ApplyBarPreferences();
        _instance.RefreshButtons();
        _instance.Show();
        _instance.Activate();
    }

    /// <summary>Re-applies Settings (visibility, size) if the bar exists. Called after Settings saves.</summary>
    public static void RefreshIfExists()
    {
        if (_instance is null) return;
        _instance.ApplyBarPreferences();
        _instance.RefreshButtons();
    }

    private void ApplyBarPreferences()
    {
        var prefs = State.Config.Preferences;
        OrganizeBtn.Visibility = prefs.ShowOrganizeOnQuickBar ? Visibility.Visible : Visibility.Collapsed;
        RenameBtn.Visibility = prefs.ShowRenameOnQuickBar ? Visibility.Visible : Visibility.Collapsed;
        var (pad, font) = prefs.QuickBarSize switch
        {
            "S" => (new Thickness(7, 3, 7, 3), 12.0),
            "L" => (new Thickness(14, 8, 14, 8), 15.0),
            _ => (new Thickness(10, 5, 10, 5), 13.0),
        };
        foreach (var button in new[] { OrganizeBtn, RenameBtn })
        {
            button.Padding = pad;
            button.FontSize = font;
        }
    }

    private void RefreshButtons()
    {
        var prefs = State.Config.Preferences;
        var (pad, font) = prefs.QuickBarSize switch
        {
            "S" => (new Thickness(7, 3, 7, 3), 12.0),
            "L" => (new Thickness(14, 8, 14, 8), 15.0),
            _ => (new Thickness(10, 5, 10, 5), 13.0),
        };

        ButtonsPanel.Children.Clear();
        var destinations = State.Config.Destinations.OrderBy(d => d.SortOrder).ToList();
        for (var i = 0; i < destinations.Count; i++)
        {
            var dest = destinations[i];
            var isChild = dest.ParentId is not null;
            var shortcut = i < 9 ? $" (Alt+{i + 1})" : "";
            var button = new Button
            {
                Style = (Style)FindResource("DestinationButton"),
                Content = prefs.QuickBarShowLabels ? $"{dest.Icon} {(isChild ? "↳ " : "")}{dest.Name}" : dest.Icon,
                Tag = dest,
                AllowDrop = true,
                Margin = new Thickness(3),
                Padding = pad,
                FontSize = font,
                ToolTip = $"Move selected file(s)/folder(s) to {dest.ExpandedPath}{shortcut}",
            };
            button.Click += Destination_Click;
            button.DragOver += (s, e) => { if (e.Data.GetDataPresent(DataFormats.FileDrop)) e.Effects = DragDropEffects.Move; };
            button.Drop += Destination_Drop;
            button.ContextMenu = BuildDestinationMenu(dest);
            ButtonsPanel.Children.Add(button);
        }
        ApplyBarPreferences(); // keep action buttons at the chosen size too
    }

    private ContextMenu BuildDestinationMenu(Destination dest)
    {
        var menu = new ContextMenu();
        MenuItem Item(string header, Action action)
        {
            var item = new MenuItem { Header = header };
            item.Click += (_, _) => action();
            menu.Items.Add(item);
            return item;
        }

        Item("Open folder", () => MainWindow.OpenFolder(dest.ExpandedPath));
        Item("Rename destination…", () =>
        {
            var name = Dialogs.Prompt("Rename destination", "New name:", dest.Name);
            if (name is null) return;
            dest.Name = name;
            State.Save();
            RefreshAll();
        });
        Item("Remove destination", () =>
        {
            if (!Dialogs.Confirm($"Remove destination \"{dest.Name}\"?\n(Files already in the folder are untouched.)")) return;
            foreach (var child in State.Config.Destinations.Where(d => d.ParentId == dest.Id))
                child.ParentId = null;
            State.Config.Destinations.Remove(dest);
            State.Save();
            RefreshAll();
        });
        return menu;
    }

    private void RefreshAll()
    {
        RefreshButtons();
        if (Application.Current.MainWindow is MainWindow main)
            main.RefreshDestinations();
    }

    private async void Destination_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not Button { Tag: Destination dest }) return;
        var items = ExplorerSelection.GetSelectedFiles(); // files AND folders
        if (items.Count == 0) return; // nothing selected in Explorer — ignore instead of popping a picker over Explorer
        var mode = State.Config.Preferences.DefaultTransferMode;
        var conflict = State.Config.Preferences.DefaultConflictPolicy;
        if (!await TransferHelper.ConfirmIfNeededAsync(this, items, dest.Name, mode)) return;
        var result = items.Count >= 10 || items.Any(Directory.Exists)
            ? await TransferHelper.RunTransferWithProgressAsync(this, items, dest, mode, conflict)
            : await Task.Run(() => State.Ops.TransferFiles(items, dest.ExpandedPath, mode, conflict));
        ShowResult($"→ {dest.Name}", result);
    }

    private async void Destination_Drop(object sender, DragEventArgs e)
    {
        if (sender is not Button { Tag: Destination dest }) return;
        if (e.Data.GetData(DataFormats.FileDrop) is string[] dropped)
        {
            var items = dropped.Where(p => File.Exists(p) || Directory.Exists(p)).ToList();
            if (items.Count == 0) return;
            var mode = State.Config.Preferences.DefaultTransferMode;
            var conflict = State.Config.Preferences.DefaultConflictPolicy;
            if (!await TransferHelper.ConfirmIfNeededAsync(this, items, dest.Name, mode)) return;
            var result = items.Count >= 10 || items.Any(Directory.Exists)
                ? await TransferHelper.RunTransferWithProgressAsync(this, items, dest, mode, conflict)
                : await Task.Run(() => State.Ops.TransferFiles(items, dest.ExpandedPath, mode, conflict));
            ShowResult($"→ {dest.Name}", result);
        }
    }

    private void ShowResult(string label, BatchResult result)
    {
        ToolTip = result.Cancelled
            ? $"⏹ Cancelled — {result.Succeeded} item(s) moved {label} (Undo in main window reverses them)"
            : result.Failed > 0
                ? $"⚠ {result.Succeeded} moved {label} · {result.Failed} failed · {result.SkippedCount} skipped"
                : $"✓ {result.Succeeded} item(s) {label}" + (result.SkippedCount > 0 ? $" · {result.SkippedCount} skipped (already existed)" : "");
        if (Application.Current.MainWindow is MainWindow main)
            main.RefreshActivity();
    }

    /// <summary>⚡ Organize: organizes the folder open in Explorer (foreground window), with the §10 preview first. No Explorer folder → small folder picker (never forces the main window open).</summary>
    private async void Organize_Click(object sender, RoutedEventArgs e)
    {
        var folder = ExplorerSelection.GetExplorerFolderPath()
                     ?? Dialogs.PickFolder("Pick the folder to organize");
        if (folder is null) return;

        List<PreviewItem> preview;
        try
        {
            preview = await Task.Run(() => CreateEngine().BuildPreview(folder, State.Config.Preferences.IncludeSubfolders));
        }
        catch (Exception ex)
        {
            Dialogs.Error(ex.Message);
            return;
        }

        if (preview.Count == 0)
        {
            Dialogs.Info($"Nothing to organize in:\n{folder}");
            return;
        }

        var window = new PreviewWindow(preview) { Owner = this };
        if (window.ShowDialog() != true) return;
        var confirmed = window.ConfirmedItems;
        if (confirmed.Count == 0) return;

        var pairs = confirmed.Select(p => (p.SourcePath, p.DestinationPath)).ToList();
        var result = await TransferHelper.RunApplyWithProgressAsync(this, pairs,
            State.Config.Preferences.DefaultTransferMode,
            State.Config.Preferences.DefaultConflictPolicy);
        Dialogs.Info(result.Cancelled
            ? $"Cancelled — {result.Succeeded} file(s) were already organized in {folder}.\n(↶ Undo in the main window reverses them.)"
            : $"Organized {result.Succeeded} file(s) in {folder}." +
              (result.Failed > 0 ? $"\nFailed: {result.Failed}" : "") +
              "\n(↶ Undo in the main window reverses this.)");
        if (Application.Current.MainWindow is MainWindow main)
            main.RefreshActivity();
    }

    /// <summary>Organize engine honouring the chosen profile (Sorted Documents tree vs Standard categories).</summary>
    internal static OrganizeEngine CreateEngine() =>
        App.State.Config.Preferences.OrganizeProfile == "SortedDocuments"
            ? new OrganizeEngine(App.State.Config, AppConfig.SortedDocumentsMap())
            : new OrganizeEngine(App.State.Config);

    /// <summary>✏ Rename: opens batch rename for Explorer's current selection (files and folders).</summary>
    private void Rename_Click(object sender, RoutedEventArgs e)
    {
        var items = ExplorerSelection.GetSelectedFiles();
        if (items.Count == 0)
        {
            Dialogs.Info("Select one or more files/folders in Explorer first, then click ✏.");
            return;
        }
        new RenameWindow(items) { Owner = this }.ShowDialog();
        if (Application.Current.MainWindow is MainWindow main)
            main.RefreshActivity();
    }

    private void Add_Click(object sender, RoutedEventArgs e)
    {
        var folder = Dialogs.PickFolder("Select a folder to add to the Quick Bar");
        if (folder is null) return;
        State.Config.Destinations.Add(new Destination
        {
            Name = Path.GetFileName(folder.TrimEnd('\\', '/')),
            Path = folder, Icon = "📁", SortOrder = State.Config.Destinations.Count,
        });
        State.Save();
        RefreshAll();
    }

    private void Home_Click(object sender, RoutedEventArgs e)
    {
        if (Application.Current.MainWindow is MainWindow main)
        {
            main.Show();
            main.Activate();
        }
    }

    /// <summary>☰ Options on the bar: opening the software/settings from here (user request).</summary>
    private void Menu_Click(object sender, RoutedEventArgs e)
    {
        var menu = new ContextMenu();

        MenuItem Item(string header, Action action)
        {
            var item = new MenuItem { Header = header };
            item.Click += (_, _) => action();
            menu.Items.Add(item);
            return item;
        }

        Item("🏠 Open File Organizer", () => Home_Click(sender, e));
        Item("⚙ Open Settings…", () =>
        {
            Home_Click(sender, e);
            new SettingsWindow { Owner = this }.ShowDialog();
            ApplyBarPreferences();
            RefreshButtons();
            if (Application.Current.MainWindow is MainWindow main)
                main.RefreshDestinations();
        });
        Item("⚡ Organize Current Folder…", () => Organize_Click(sender, e));
        Item("✏ Rename Selection…", () => Rename_Click(sender, e));
        menu.Items.Add(new Separator());
        Item("Hide Quick Bar", () => Hide());

        menu.PlacementTarget = MenuBtn;
        menu.IsOpen = true;
    }

    private void Close_Click(object sender, RoutedEventArgs e) => Hide();

    private void Window_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (e.ButtonState == MouseButtonState.Pressed) DragMove();
    }
}
