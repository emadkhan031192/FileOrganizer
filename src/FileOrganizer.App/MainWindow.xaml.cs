using System.Diagnostics;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using FileOrganizer.Core;

namespace FileOrganizer.App;

public partial class MainWindow : Window
{
    private AppState State => App.State;

    public MainWindow()
    {
        InitializeComponent();
        FolderBox.Text = KnownFolder.Downloads;
        RefreshDestinations();
        RefreshActivity();
        RefreshRecentFolders();
    }

    // ---------- Quick Destinations (§1/§6/§12) ----------

    internal void RefreshDestinations()
    {
        DestinationsPanel.Children.Clear();
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
                        new TextBlock { Text = (isChild ? "↳ " : "") + dest.Name, VerticalAlignment = VerticalAlignment.Center },
                    },
                },
            };
            if (!string.IsNullOrWhiteSpace(dest.ColorHex) &&
                ColorConverter.ConvertFromString(dest.ColorHex) is Color color)
                button.Background = new SolidColorBrush(color);

            button.Click += DestinationButton_Click;
            button.DragOver += (s, e) => { if (e.Data.GetDataPresent(DataFormats.FileDrop)) e.Effects = DragDropEffects.Move; };
            button.Drop += DestinationButton_Drop;
            button.ContextMenu = BuildDestinationContextMenu(dest);
            DestinationsPanel.Children.Add(button);
        }

        var add = new Button { Content = "+ Add Destination", Style = (Style)FindResource("DestinationButton") };
        add.Click += AddDestination_Click;
        DestinationsPanel.Children.Add(add);
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
            !Dialogs.Confirm($"Replace mode: existing files in \"{dest.Name}\" with the same name WILL be overwritten. Continue?"))
            return;

        SetStatus($"Moving {files.Count} file(s) to {dest.Name}…");
        var result = await Task.Run(() => State.Ops.TransferFiles(files, dest.ExpandedPath, mode, conflict));
        SetStatus($"✓ {result.Succeeded} file(s) → {dest.Name}" +
                  (result.SkippedCount > 0 ? $" · {result.SkippedCount} skipped (already existed)" : "") +
                  (result.Failed > 0 ? $" · {result.Failed} failed: {FirstError(result)}" : ""));
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
            var engine = new OrganizeEngine(State.Config);
            var dateOptions = byDate
                ? new DateOrganizeOptions { Enabled = true, Source = State.Config.Preferences.OrganizeDateSource, AppendCategoryFolder = true }
                : new DateOrganizeOptions();
            preview = await Task.Run(() => engine.BuildPreview(folder, State.Config.Preferences.IncludeSubfolders, dateOptions));
        }
        catch (Exception ex) { Dialogs.Error(ex.Message); SetStatus("Ready."); return; }

        if (preview.Count == 0) { SetStatus("Nothing to organize — everything is already in place."); return; }

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
        var progress = new Progress<(int Done, int Total, string CurrentFile)>(
            p => SetStatus($"Organizing {p.Done}/{p.Total} — {p.CurrentFile}"));
        var result = await Task.Run(() => State.Ops.ApplyPairs(
            confirmed.Select(p => (p.SourcePath, p.DestinationPath)), mode, conflict, progress));
        SetStatus($"✓ Organized {result.Succeeded} file(s)" +
                  (result.SkippedCount > 0 ? $" · {result.SkippedCount} skipped" : "") +
                  (result.Failed > 0 ? $" · {result.Failed} failed: {FirstError(result)}" : "") +
                  "  (↶ Undo reverses this)");
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
        new RenameWindow(files) { Owner = this }.ShowDialog();
        RefreshActivity();
    }

    private void DuplicatesBtn_Click(object sender, RoutedEventArgs e)
    {
        new DuplicatesWindow(FolderBox.Text.Trim()) { Owner = this }.ShowDialog();
        RefreshActivity();
    }

    private void RulesBtn_Click(object sender, RoutedEventArgs e) => new RulesWindow() { Owner = this }.ShowDialog();

    private void QuickBarBtn_Click(object sender, RoutedEventArgs e) => QuickBarWindow.ShowBar();

    private void SettingsBtn_Click(object sender, RoutedEventArgs e)
    {
        new SettingsWindow() { Owner = this }.ShowDialog();
        RefreshDestinations();
    }

    private async void UndoBtn_Click(object sender, RoutedEventArgs e)
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
