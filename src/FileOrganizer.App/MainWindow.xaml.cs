using System.Diagnostics;
using System.IO;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using FileOrganizer.Core;

namespace FileOrganizer.App;

public partial class MainWindow : Window
{
    private AppState State => App.State;

    // ----- Global hotkeys (feature 2): Alt+1..Alt+9 → destinations 1..9 -----
    private const int WmHotkey = 0x0312;
    private const uint ModAlt = 0x0001;
    private const int HotkeyBaseId = 9001;
    private HwndSource? _hwndSource;
    private readonly List<int> _registeredHotkeys = new();

    [DllImport("user32.dll", SetLastError = true)]
    private static extern bool RegisterHotKey(IntPtr hWnd, int id, uint fsModifiers, uint vk);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern bool UnregisterHotKey(IntPtr hWnd, int id);

    public MainWindow()
    {
        InitializeComponent();
        FolderBox.Text = KnownFolder.Downloads;
        RefreshDestinations();
        RefreshActivity();
        RefreshRecentFolders();
        WatchCheck.IsChecked = State.Config.Preferences.WatchDownloads;
        if (State.Config.Preferences.WatchDownloads)
            StartWatching();
        Loaded += (_, _) => ShowFirstRunOnce();
        SourceInitialized += MainWindow_SourceInitialized;
        Closed += MainWindow_Closed;
        // Phase 2 wheel entry: the low-level hook (scoped, never logs keys)
        // is what makes Ctrl+` and drag-flow wheel keys work from Explorer.
        Loaded += (_, _) => WheelHotkeyService.Start();
    }

    private void ShowFirstRunOnce()
    {
        if (State.Config.Preferences.SetupDone) return;
        new FirstRunWindow { Owner = this }.ShowDialog();
        WatchCheck.IsChecked = State.Config.Preferences.WatchDownloads;
        if (State.Config.Preferences.WatchDownloads) StartWatching(); else StopWatching();
        QuickBarWindow.RefreshIfExists();
        RefreshDestinations();
    }

    private void MainWindow_SourceInitialized(object? sender, EventArgs e)
    {
        var handle = new WindowInteropHelper(this).Handle;
        _hwndSource = HwndSource.FromHwnd(handle);
        _hwndSource?.AddHook(WndProc);
        RegisterDestinationHotkeys(handle);
    }

    private void RegisterDestinationHotkeys(IntPtr handle)
    {
        foreach (var id in _registeredHotkeys)
            UnregisterHotKey(handle, id);
        _registeredHotkeys.Clear();

        if (!State.Config.Preferences.EnableHotkeys)
            return;

        var count = Math.Min(9, State.Config.Destinations.Count);
        for (var i = 0; i < count; i++)
        {
            var id = HotkeyBaseId + i;
            // VK_1..VK_9 = 0x31..0x39. A failed registration (shortcut taken by another app) is skipped silently.
            if (RegisterHotKey(handle, id, ModAlt, (uint)(0x31 + i)))
                _registeredHotkeys.Add(id);
        }
    }

    private IntPtr WndProc(IntPtr hwnd, int msg, IntPtr wParam, IntPtr lParam, ref bool handled)
    {
        if (msg == WmHotkey)
        {
            var index = wParam.ToInt32() - HotkeyBaseId;
            var destinations = State.Config.Destinations.OrderBy(d => d.SortOrder).ToList();
            if (index >= 0 && index < destinations.Count)
            {
                _ = HotkeyMoveAsync(destinations[index]);
                handled = true;
            }
        }
        return IntPtr.Zero;
    }

    /// <summary>Alt+N move: Explorer's current selection (files/folders) → destination N, with Quick Bar feedback.</summary>
    private async Task HotkeyMoveAsync(Destination dest)
    {
        var items = ExplorerSelection.GetSelectedFiles();
        if (items.Count == 0)
        {
            SetStatus($"Alt shortcut for {dest.Name}: nothing is selected in Explorer.");
            return;
        }
        var mode = State.Config.Preferences.DefaultTransferMode;
        var conflict = State.Config.Preferences.DefaultConflictPolicy;
        if (!await TransferHelper.ConfirmIfNeededAsync(this, items, dest.Name, mode))
            return;
        var result = items.Count >= 10 || items.Any(Directory.Exists)
            ? await TransferHelper.RunTransferWithProgressAsync(this, items, dest, mode, conflict)
            : await Task.Run(() => State.Ops.TransferFiles(items, dest.ExpandedPath, mode, conflict));
        SetStatus(result.Cancelled
            ? $"⏹ Alt+ move cancelled — {result.Succeeded} item(s) already moved to {dest.Name}"
            : $"✓ {result.Succeeded} item(s) → {dest.Name} (hotkey)" +
              (result.SkippedCount > 0 ? $" · {result.SkippedCount} skipped" : "") +
              (result.Failed > 0 ? $" · {result.Failed} failed" : ""));
        RefreshActivity();
    }

    internal void ReregisterHotkeys()
    {
        var handle = new WindowInteropHelper(this).Handle;
        if (handle != IntPtr.Zero)
            RegisterDestinationHotkeys(handle);
    }

    private void MainWindow_Closed(object? sender, EventArgs e)
    {
        StopWatching();
        WheelHotkeyService.Stop();
        var handle = new WindowInteropHelper(this).Handle;
        foreach (var id in _registeredHotkeys)
            UnregisterHotKey(handle, id);
        _registeredHotkeys.Clear();
        _hwndSource?.RemoveHook(WndProc);
    }

    // ---------- Quick Destinations (§1/§6/§12) ----------

    internal void RefreshDestinations()
    {
        DestinationsPanel.Children.Clear();
        var lastUsed = State.Config.Destinations
            .Where(d => d.LastUsedUtc is not null).MaxBy(d => d.LastUsedUtc)?.Id;
        foreach (var dest in OrderedDestinations())
        {
            var isChild = dest.ParentId is not null;
            var button = new Button
            {
                Style = (Style)FindResource("DestinationButton"),
                Tag = dest,
                AllowDrop = true,
                Margin = isChild ? new Thickness(18, 4, 4, 4) : new Thickness(4),
                Content = new StackPanel
                {
                    Orientation = Orientation.Horizontal,
                    Children =
                    {
                        new TextBlock { Text = dest.Icon, FontSize = 15, Margin = new Thickness(0, 0, 6, 0) },
                        new TextBlock { Text = (dest.IsFavorite ? "★ " : "") + (isChild ? "↳ " : "") + dest.Name, VerticalAlignment = VerticalAlignment.Center },
                    },
                },
            };
            if (!string.IsNullOrWhiteSpace(dest.ColorHex) &&
                ColorConverter.ConvertFromString(dest.ColorHex) is Color color)
                button.Background = new SolidColorBrush(color);
            else if (dest.Id == lastUsed)
                button.Background = (System.Windows.Media.Brush)FindResource("AccentSoft"); // last-used highlight

            button.Click += DestinationButton_Click;
            button.DragOver += (s, e) => { if (e.Data.GetDataPresent(DataFormats.FileDrop)) e.Effects = DragDropEffects.Move; };
            button.Drop += DestinationButton_Drop;
            button.ContextMenu = BuildDestinationContextMenu(dest);
            DestinationsPanel.Children.Add(button);
        }

        var add = new Button { Content = "+ Add Destination", Style = (Style)FindResource("DestinationButton") };
        add.Click += AddDestination_Click;
        DestinationsPanel.Children.Add(add);

        if (State.Config.Preferences.RecycleBinLocation != "None")
        {
            var recycle = new Button
            {
                Content = "🗑 Recycle Bin",
                Style = (Style)FindResource("DestinationButton"),
                AllowDrop = true,
                Background = new System.Windows.Media.SolidColorBrush(System.Windows.Media.Color.FromRgb(0xFE, 0xE2, 0xE2)),
                ToolTip = "Send selected file(s)/folder(s) to the Windows Recycle Bin (restore from there, not via Undo)",
            };
            recycle.Click += async (_, _) =>
            {
                var items = ExplorerSelection.GetSelectedFiles();
                if (items.Count == 0) { SetStatus("Nothing selected in Explorer to recycle."); return; }
                ToastWindow.Show(await RecycleBinService.RecycleAsync(items));
                RefreshActivity();
            };
            recycle.DragOver += (s, e) => { if (e.Data.GetDataPresent(DataFormats.FileDrop)) e.Effects = DragDropEffects.Move; };
            recycle.Drop += async (_, e) =>
            {
                if (e.Data.GetData(DataFormats.FileDrop) is string[] dropped)
                {
                    var items = dropped.Where(p => File.Exists(p) || Directory.Exists(p)).ToList();
                    if (items.Count == 0) return;
                    ToastWindow.Show(await RecycleBinService.RecycleAsync(items));
                    RefreshActivity();
                }
            };
            DestinationsPanel.Children.Add(recycle);
        }
    }

    /// <summary>Parents first (by SortOrder), each followed by its children — so sub-destinations stay grouped (§6).</summary>
    private List<Destination> OrderedDestinations()
    {
        var all = State.Config.Destinations;
        var ordered = new List<Destination>();
        foreach (var parent in all.Where(d => d.ParentId is null).OrderBy(d => d.SortOrder))
        {
            ordered.Add(parent);
            ordered.AddRange(all.Where(d => d.ParentId == parent.Id).OrderBy(d => d.SortOrder));
        }
        // Orphans (parent deleted) still show.
        ordered.AddRange(all.Where(d => !ordered.Contains(d)).OrderBy(d => d.SortOrder));
        return ordered;
    }

    private ContextMenu BuildDestinationContextMenu(Destination dest)
    {
        var menu = new ContextMenu();

        MenuItem Item(string header, Action action)
        {
            var item = new MenuItem { Header = header };
            item.Click += (_, _) => action();
            menu.Items.Add(item);
            return item;
        }

        Item("Open folder", () => OpenFolder(dest.ExpandedPath));
        Item(dest.IsFavorite ? "★ Unpin favourite" : "☆ Pin as favourite", () =>
        {
            dest.IsFavorite = !dest.IsFavorite; State.Save(); RefreshDestinations();
        });
        Item("Rename…", () =>
        {
            var name = Dialogs.Prompt("Rename destination", "New name:", dest.Name);
            if (name is not null) { dest.Name = name; State.Save(); RefreshDestinations(); }
        });
        Item("Change icon…", () =>
        {
            var icon = Dialogs.Prompt("Change icon", "Emoji or short text (e.g. 💼):", dest.Icon);
            if (icon is not null) { dest.Icon = icon; State.Save(); RefreshDestinations(); }
        });
        Item("Change colour… (#RRGGBB, blank = default)", () =>
        {
            var color = Dialogs.Prompt("Change colour", "Colour as #RRGGBB (leave blank for default):", dest.ColorHex) ?? "";
            dest.ColorHex = color; State.Save(); RefreshDestinations();
        });
        Item("Add sub-destination…", () =>
        {
            var folder = Dialogs.PickFolder("Select the sub-destination folder");
            if (folder is null) return;
            State.Config.Destinations.Add(new Destination
            {
                Name = Path.GetFileName(folder.TrimEnd('\\', '/')),
                Path = folder, Icon = "📁", ParentId = dest.Id,
                SortOrder = State.Config.Destinations.Count,
            });
            State.Save(); RefreshDestinations();
            SetStatus($"Added sub-destination under {dest.Name}.");
        });
        Item("Move left", () => Reorder(dest, -1));
        Item("Move right", () => Reorder(dest, +1));
        Item("Remove", () =>
        {
            if (!Dialogs.Confirm($"Remove destination \"{dest.Name}\"?\n(Files already in the folder are untouched.)")) return;
            foreach (var child in State.Config.Destinations.Where(d => d.ParentId == dest.Id))
                child.ParentId = null;
            State.Config.Destinations.Remove(dest);
            State.Save(); RefreshDestinations();
        });
        return menu;
    }

    private void Reorder(Destination dest, int delta)
    {
        var siblings = State.Config.Destinations
            .Where(d => d.ParentId == dest.ParentId).OrderBy(d => d.SortOrder).ToList();
        var index = siblings.IndexOf(dest);
        var target = index + delta;
        if (index < 0 || target < 0 || target >= siblings.Count) return;
        (siblings[index].SortOrder, siblings[target].SortOrder) = (siblings[target].SortOrder, siblings[index].SortOrder);
        State.Save(); RefreshDestinations();
    }

    private async void DestinationButton_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not Button { Tag: Destination dest }) return;
        var files = ExplorerSelection.GetSelectedFiles(); // files AND folders
        if (files.Count == 0)
            files = Dialogs.PickFiles($"No file/folder is selected in Explorer. Pick file(s) to move to {dest.Name}:");
        if (files.Count == 0) { SetStatus("No files selected — nothing moved."); return; }
        await TransferAsync(files, dest);
    }

    private async void DestinationButton_Drop(object sender, DragEventArgs e)
    {
        if (sender is not Button { Tag: Destination dest }) return;
        if (e.Data.GetData(DataFormats.FileDrop) is string[] files && files.Length > 0)
            await TransferAsync(files.Where(p => File.Exists(p) || Directory.Exists(p)).ToList(), dest);
    }

    private async Task TransferAsync(List<string> files, Destination dest)
    {
        var mode = ParseEnum<TransferMode>(TransferModeCombo, TransferMode.Move);
        var conflict = ParseEnum<ConflictPolicy>(ConflictCombo, ConflictPolicy.AutoRename);
        if (conflict == ConflictPolicy.Replace &&
            !Dialogs.Confirm($"Replace mode: existing files/folders in \"{dest.Name}\" with the same name WILL be overwritten. Continue?"))
            return;

        if (!await TransferHelper.ConfirmIfNeededAsync(this, files, dest.Name, mode))
        {
            SetStatus("Move cancelled before starting — nothing moved.");
            return;
        }

        SetStatus($"Moving {files.Count} item(s) to {dest.Name}…");
        var result = await TransferHelper.RunTransferWithProgressAsync(this, files, dest, mode, conflict);
        if (result.Succeeded > 0)
            State.RecordDestinationUsed(dest);
        SetStatus(result.Cancelled
            ? $"⏹ Cancelled — {result.Succeeded} item(s) already moved to {dest.Name} (↶ Undo reverses them)"
            : $"✓ {result.Succeeded} item(s) → {dest.Name}" +
              (result.SkippedCount > 0 ? $" · {result.SkippedCount} skipped (already existed)" : "") +
              (result.Failed > 0 ? $" · {result.Failed} failed: {FirstError(result)}" : ""));
        if (!result.Cancelled && result.Succeeded > 0)
            ToastWindow.Show($"✓ {result.Succeeded} item(s) → {dest.Name}" +
                (result.Failed > 0 ? $" · {result.Failed} failed: {FirstError(result)}" : ""),
                onUndo: () => _ = UndoLast());
        RefreshActivity();
    }

    private static string? FirstError(BatchResult r) => r.Files.FirstOrDefault(f => !f.Success && !f.Skipped)?.Error;

    private static T ParseEnum<T>(ComboBox combo, T fallback) where T : struct, Enum =>
        (combo.SelectedItem as ComboBoxItem)?.Tag is string tag && Enum.TryParse<T>(tag, out var value) ? value : fallback;

    // ---------- Add destination ----------

    private void AddDestination_Click(object sender, RoutedEventArgs e)
    {
        var folder = Dialogs.PickFolder("Select a folder to save as a Quick Destination");
        if (folder is null) return;
        var name = Dialogs.Prompt("New destination", "Button label:", Path.GetFileName(folder.TrimEnd('\\', '/')));
        if (name is null) return;
        State.Config.Destinations.Add(new Destination
        {
            Name = name, Path = folder, Icon = "📁", SortOrder = State.Config.Destinations.Count,
        });
        State.Save(); RefreshDestinations();
        SetStatus($"Added destination {name}. Tip: Settings → Install Explorer Context Menu to refresh right-click entries.");
    }

    // ---------- Selected folder / Organize / Search ----------

    private void BrowseBtn_Click(object sender, RoutedEventArgs e)
    {
        var folder = Dialogs.PickFolder("Select the folder to organize / search");
        if (folder is not null) { FolderBox.Text = folder; RememberFolder(folder); }
    }

    private void FolderBox_LostFocus(object sender, RoutedEventArgs e) => RememberFolder(FolderBox.Text.Trim());

    private void RecentCombo_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (RecentCombo.SelectedItem is string folder) FolderBox.Text = folder;
    }

    private void RememberFolder(string folder)
    {
        if (!Directory.Exists(folder)) return;
        State.ConfigService.RememberFolder(State.Config, folder);
        State.Save();
        RefreshRecentFolders();
    }

    private void RefreshRecentFolders()
    {
        var current = FolderBox.Text;
        RecentCombo.ItemsSource = State.Config.RecentFolders.ToList();
        FolderBox.Text = current;
    }

    private async void OrganizeBtn_Click(object sender, RoutedEventArgs e) => await OrganizeAsync(byDate: false);

    private async void OrganizeDateBtn_Click(object sender, RoutedEventArgs e) => await OrganizeAsync(byDate: true);

    private async Task OrganizeAsync(bool byDate)
    {
        var folder = FolderBox.Text.Trim();
        if (!Directory.Exists(folder)) { Dialogs.Error($"Folder not found:\n{folder}"); return; }
        RememberFolder(folder);

        SetStatus("Scanning…");
        List<PreviewItem> preview;
        try
        {
            var engine = QuickBarWindow.CreateEngine();
            var dateOptions = byDate
                ? new DateOrganizeOptions { Enabled = true, Source = State.Config.Preferences.OrganizeDateSource, AppendCategoryFolder = true }
                : new DateOrganizeOptions();
            preview = await Task.Run(() => engine.BuildPreview(folder, State.Config.Preferences.IncludeSubfolders, dateOptions));
        }
        catch (Exception ex) { Dialogs.Error(ex.Message); SetStatus("Ready."); return; }

        if (preview.Count == 0) { SetStatus("Nothing to organize — everything is already in place."); return; }

        // Dry run (recommendation 6): the preview IS the result — nothing moves.
        if (DryRunCheck.IsChecked == true)
        {
            SetStatus($"Dry run — {preview.Count} file(s) · {PathHelpers.FormatSize(preview.Sum(p => p.SizeBytes))} would move. Nothing moved.");
            return;
        }

        List<PreviewItem> confirmed = preview;
        if (State.Config.Preferences.ConfirmBeforeOrganize)
        {
            var window = new PreviewWindow(preview) { Owner = this };
            if (window.ShowDialog() != true) { SetStatus("Organize cancelled — nothing moved."); return; }
            confirmed = window.ConfirmedItems;
            if (confirmed.Count == 0) { SetStatus("Nothing selected — nothing moved."); return; }
        }

        var mode = ParseEnum<TransferMode>(TransferModeCombo, TransferMode.Move);
        var conflict = ParseEnum<ConflictPolicy>(ConflictCombo, ConflictPolicy.AutoRename);
        var pairs = confirmed.Select(p => (p.SourcePath, p.DestinationPath)).ToList();
        var result = await TransferHelper.RunApplyWithProgressAsync(this, pairs, mode, conflict);
        SetStatus(result.Cancelled
            ? $"⏹ Cancelled — {result.Succeeded} file(s) already organized (↶ Undo reverses them)"
            : $"✓ Organized {result.Succeeded} file(s)" +
              (result.SkippedCount > 0 ? $" · {result.SkippedCount} skipped" : "") +
              (result.Failed > 0 ? $" · {result.Failed} failed: {FirstError(result)}" : "") +
              "  (↶ Undo reverses this)");
        if (!result.Cancelled && result.Succeeded > 0)
            ToastWindow.Show($"✓ Organized {result.Succeeded} file(s)", onUndo: () => _ = UndoLast());
        RefreshActivity();
    }

    private async void SearchBtn_Click(object sender, RoutedEventArgs e) => await SearchAsync();

    private void SearchBox_KeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Enter) _ = SearchAsync();
    }

    private async Task SearchAsync()
    {
        var folder = FolderBox.Text.Trim();
        if (!Directory.Exists(folder)) { Dialogs.Error($"Folder not found:\n{folder}"); return; }
        var query = new SearchQuery { NameContains = SearchBox.Text.Trim(), Recursive = true };
        SetStatus("Searching…");
        var hits = await Task.Run(() => SearchService.Search(folder, query).Take(500).Select(f => f.FullName).ToList());
        SearchResultsList.ItemsSource = hits;
        SetStatus($"Found {hits.Count} file(s). Double-click a result to open its folder.");
    }

    private void SearchResultsList_MouseDoubleClick(object sender, MouseButtonEventArgs e)
    {
        if (SearchResultsList.SelectedItem is string path)
        {
            var folder = Path.GetDirectoryName(path);
            if (folder is not null) OpenFolder(folder, selectFile: path);
        }
    }

    // ---------- Rename / Duplicates / Rules / Undo / Settings ----------

    private void RenameBtn_Click(object sender, RoutedEventArgs e)
    {
        var files = Dialogs.GetFilesToActOn();
        if (files.Count == 0) { SetStatus("No files selected for rename."); return; }
        new RenameWizardWindow(files) { Owner = this }.ShowDialog();
        RefreshActivity();
    }

    /// <summary>Simple Rename, back up front: one file, just type its new name (the wizard shows that panel for a single file).</summary>
    private void SimpleRenameBtn_Click(object sender, RoutedEventArgs e)
    {
        var files = Dialogs.GetFilesToActOn();
        if (files.Count == 0) { SetStatus("No files selected for rename."); return; }
        if (files.Count > 1)
        {
            SetStatus("Simple Rename works on one file — select just one, or use ✏️ Rename for many.");
            Dialogs.Info("Simple Rename works on a single file.\nSelect just one file in Explorer, or use ✏️ Rename for many files at once.");
            return;
        }
        new RenameWizardWindow(files) { Owner = this }.ShowDialog();
        RefreshActivity();
    }

    private void DuplicatesBtn_Click(object sender, RoutedEventArgs e)
    {
        new DuplicatesWindow(FolderBox.Text.Trim()) { Owner = this }.ShowDialog();
        RefreshActivity();
    }

    private void RulesBtn_Click(object sender, RoutedEventArgs e) => new RulesWindow() { Owner = this }.ShowDialog();

    private void QuickBarBtn_Click(object sender, RoutedEventArgs e) => QuickBarWindow.ShowBar();

    private void WheelBtn_Click(object sender, RoutedEventArgs e) => WheelWindow.ShowMove();

    private void HistoryBtn_Click(object sender, RoutedEventArgs e) =>
        new UndoHistoryWindow { Owner = this }.ShowDialog();

    private void DropZone_DragOver(object sender, DragEventArgs e)
    {
        if (e.Data.GetDataPresent(DataFormats.FileDrop)) e.Effects = DragDropEffects.Move;
    }

    private void DropZone_Drop(object sender, DragEventArgs e)
    {
        if (e.Data.GetData(DataFormats.FileDrop) is string[] dropped)
            WheelWindow.ShowMove(dropped.Where(p => File.Exists(p) || Directory.Exists(p)).ToList());
    }

    // ---------- Watch folder (recommendation 6) ----------

    private FileSystemWatcher? _watcher;
    private readonly HashSet<string> _watchQueue = new(StringComparer.OrdinalIgnoreCase);
    private System.Windows.Threading.DispatcherTimer? _watchTimer;

    private void WatchCheck_Changed(object sender, RoutedEventArgs e)
    {
        // Fires once during XAML load too — only persist/react after the window exists.
        if (!IsLoaded && WatchCheck.IsChecked is null) return;
        State.Config.Preferences.WatchDownloads = WatchCheck.IsChecked == true;
        State.Save();
        if (WatchCheck.IsChecked == true) StartWatching(); else StopWatching();
    }

    private void StartWatching()
    {
        StopWatching();
        var downloads = KnownFolder.Downloads;
        if (!Directory.Exists(downloads)) return;
        _watcher = new FileSystemWatcher(downloads) { EnableRaisingEvents = true, IncludeSubdirectories = false };
        WatchDot.Visibility = Visibility.Visible;
        _watcher.Created += (_, args) =>
        {
            lock (_watchQueue) _watchQueue.Add(args.FullPath);
            Dispatcher.Invoke(() =>
            {
                _watchTimer ??= new System.Windows.Threading.DispatcherTimer { Interval = TimeSpan.FromSeconds(3) };
                _watchTimer.Tick -= WatchTimer_Tick;
                _watchTimer.Tick += WatchTimer_Tick;
                _watchTimer.Start(); // debounce: wait for the download to finish writing
            });
        };
        SetStatus($"Watching Downloads — new files will auto-sort (Undo still works).");
    }

    private void StopWatching()
    {
        _watchTimer?.Stop();
        WatchDot.Visibility = Visibility.Collapsed;
        if (_watcher is not null) { _watcher.EnableRaisingEvents = false; _watcher.Dispose(); _watcher = null; }
    }

    private async void WatchTimer_Tick(object? sender, EventArgs e)
    {
        _watchTimer?.Stop();
        List<string> queued;
        lock (_watchQueue) { queued = _watchQueue.ToList(); _watchQueue.Clear(); }
        var downloads = KnownFolder.Downloads;
        List<PreviewItem> preview;
        try
        {
            preview = await Task.Run(() => QuickBarWindow.CreateEngine()
                .BuildPreview(downloads, State.Config.Preferences.IncludeSubfolders))
                .ContinueWith(t => t.Result.Where(p => queued.Contains(p.SourcePath, StringComparer.OrdinalIgnoreCase)).ToList());
        }
        catch { return; }
        if (preview.Count == 0) return;
        var result = await TransferHelper.RunApplyWithProgressAsync(this,
            preview.Select(p => (p.SourcePath, p.DestinationPath)).ToList(),
            State.Config.Preferences.DefaultTransferMode, State.Config.Preferences.DefaultConflictPolicy);
        SetStatus($"Auto-sorted {result.Succeeded} new file(s) from Downloads (↶ Undo reverses this).");
        ToastWindow.Show($"✓ Auto-sorted {result.Succeeded} new file(s) from Downloads", onUndo: () => _ = UndoLast());
        RefreshActivity();
    }

    private void SettingsBtn_Click(object sender, RoutedEventArgs e)
    {
        new SettingsWindow() { Owner = this }.ShowDialog();
        RefreshDestinations();
    }

    /// <summary>Phase 1 gallery preview (design system only; the hub redesign is Phase 3).</summary>
    private void GalleryBtn_Click(object sender, RoutedEventArgs e) => new GalleryWindow().Show();

    private async void UndoBtn_Click(object sender, RoutedEventArgs e) => await UndoLast();

    internal async Task UndoLast()
    {
        try
        {
            var result = await Task.Run(() => State.Ops.UndoLastBatch());
            SetStatus($"↶ Undid {result.Succeeded} file operation(s)" + (result.Failed > 0 ? $" · {result.Failed} could not be reversed" : ""));
        }
        catch (InvalidOperationException)
        {
            SetStatus("Nothing to undo.");
        }
        RefreshActivity();
    }

    // ---------- Helpers ----------

    internal void RefreshActivity()
    {
        ActivityList.ItemsSource = State.History.Recent(50)
            .Select(en => $"{(en.Undone ? "↩" : "✓")} {Path.GetFileName(en.DestinationPath)} → {Path.GetDirectoryName(en.DestinationPath)}  · {en.TimestampUtc.ToLocalTime():HH:mm}")
            .ToList();
    }

    internal void SetStatus(string text) => StatusText.Text = text;

    internal static void OpenFolder(string folder, string? selectFile = null)
    {
        try
        {
            if (!Directory.Exists(folder)) Directory.CreateDirectory(folder);
            var args = selectFile is not null ? $"/select,\"{selectFile}\"" : $"\"{folder}\"";
            Process.Start(new ProcessStartInfo("explorer.exe", args) { UseShellExecute = true });
        }
        catch (Exception ex) { Dialogs.Error(ex.Message); }
    }

    private static class KnownFolder
    {
        public static string Downloads
        {
            get
            {
                var profile = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
                var downloads = Path.Combine(profile, "Downloads");
                return Directory.Exists(downloads) ? downloads : profile;
            }
        }
    }
}
