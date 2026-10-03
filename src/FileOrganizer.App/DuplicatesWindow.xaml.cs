using System.IO;
using System.Windows;
using System.Windows.Controls;
using FileOrganizer.Core;

namespace FileOrganizer.App;

public partial class DuplicatesWindow : Window
{
    private List<DuplicateGroup> _groups = new();

    public DuplicatesWindow(string folder)
    {
        InitializeComponent();
        FolderBox.Text = folder;
    }

    private void Browse_Click(object sender, RoutedEventArgs e)
    {
        var folder = Dialogs.PickFolder("Select the folder to scan for duplicates");
        if (folder is not null) FolderBox.Text = folder;
    }

    private async void Scan_Click(object sender, RoutedEventArgs e)
    {
        var folder = FolderBox.Text.Trim();
        if (!Directory.Exists(folder)) { Dialogs.Error($"Folder not found:\n{folder}"); return; }
        StatusText.Text = "Scanning (hashing files)…";
        GroupsList.ItemsSource = null;
        _groups = await Task.Run(() => DuplicateFinder.FindDuplicates(folder));
        GroupsList.ItemsSource = _groups.SelectMany((g, gi) =>
            new[] { $"── Group {gi + 1} · {PathHelpers.FormatSize(g.SizeBytes)} each · wastes {PathHelpers.FormatSize(g.WastedBytes)}" }
                .Concat(g.Files.Select(f => $"    {f}"))).ToList();
        var wasted = _groups.Sum(g => g.WastedBytes);
        StatusText.Text = _groups.Count == 0
            ? "No exact duplicates found."
            : $"{_groups.Count} duplicate group(s) · {PathHelpers.FormatSize(wasted)} recoverable. Choose a Keep strategy, then move or delete the rest.";
    }

    private DuplicateKeepStrategy Strategy =>
        (KeepCombo.SelectedItem as ComboBoxItem)?.Tag is string tag && Enum.TryParse<DuplicateKeepStrategy>(tag, out var s)
            ? s : DuplicateKeepStrategy.KeepFirst;

    private List<string> RemovalCandidates() =>
        _groups.SelectMany(g =>
        {
            var keeper = DuplicateFinder.PickKeeper(g, Strategy);
            return g.Files.Where(f => !string.Equals(f, keeper, StringComparison.OrdinalIgnoreCase));
        }).ToList();

    private async void MoveDuplicates_Click(object sender, RoutedEventArgs e)
    {
        var candidates = RemovalCandidates();
        if (candidates.Count == 0) { Dialogs.Info("Scan first — there are no duplicates to move."); return; }
        var trash = Dialogs.PickFolder("Move duplicates INTO this folder (they are kept, not deleted)");
        if (trash is null) return;
        var result = await Task.Run(() => App.State.Ops.TransferFiles(candidates, trash, TransferMode.Move, ConflictPolicy.AutoRename));
        StatusText.Text = $"Moved {result.Succeeded} duplicate file(s) to the trash folder. (↶ Undo reverses this.)";
    }

    private async void DeleteDuplicates_Click(object sender, RoutedEventArgs e)
    {
        var candidates = RemovalCandidates();
        if (candidates.Count == 0) { Dialogs.Info("Scan first — there are no duplicates to delete."); return; }
        // §8/§15: never permanently delete without an explicit confirmation.
        if (!Dialogs.Confirm($"Permanently delete {candidates.Count} duplicate file(s)?\nKeepers ({Strategy}) stay untouched.\nThis cannot be undone from File Organizer."))
            return;
        var deleted = 0;
        await Task.Run(() =>
        {
            foreach (var file in candidates)
            {
                try { File.Delete(file); deleted++; } catch (IOException) { } catch (UnauthorizedAccessException) { }
            }
        });
        StatusText.Text = $"Deleted {deleted} duplicate file(s). Run Scan again to refresh.";
        GroupsList.ItemsSource = null;
    }
}
