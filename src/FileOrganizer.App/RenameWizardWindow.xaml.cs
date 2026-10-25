using System.IO;
using System.Windows;
using System.Windows.Controls;
using FileOrganizer.Core;

namespace FileOrganizer.App;

/// <summary>
/// v3 rename: a 3-step wizard in plain words instead of the old control panel.
/// Step 1 files → Step 2 pick ONE recipe → Step 3 live before→after.
/// The old full control panel is still available behind "Advanced…" — nothing is lost.
/// </summary>
public partial class RenameWizardWindow : Window
{
    private readonly List<string> _files;
    private List<RenamePreviewItem> _preview = new();

    private static readonly (string Name, string Help)[] Recipes =
    {
        ("Add words", "Add your words to the start or end of every name. Example: Holiday → Holiday_Beach."),
        ("Replace words", "Swap one word for another in every name. Example: IMG → Holiday."),
        ("Add numbers", "Number the files in order: 001, 002, 003… Great for photos."),
        ("Add date", "Add the file's date to every name. Example: photo → 2026-10-25_photo."),
        ("Make web-safe", "Lowercase, spaces become hyphens, odd characters removed. Good for uploading."),
        ("Capitalize words", "Every word starts with a capital letter."),
        ("Clean up", "Underscores become spaces, so IMG_001 becomes IMG 001."),
    };

    public RenameWizardWindow(List<string> files)
    {
        InitializeComponent();
        _files = files;
        Title = $"Rename — {_files.Count} file(s)/folder(s)";
        FilesText.Text = string.Join(", ", _files.Select(p => Path.GetFileName(p.TrimEnd('\\', '/'))).Take(5))
            + (_files.Count > 5 ? $" … and {_files.Count - 5} more" : "");
        RecipeList.ItemsSource = Recipes.Select(r => r.Name).ToList();
        RecipeList.SelectedIndex = 0;

        // Single file: just type the new name (the old ✎ Simple Rename, now front and centre).
        if (_files.Count == 1)
        {
            SimplePanel.Visibility = Visibility.Visible;
            SimpleNameBox.Text = Path.GetFileName(_files[0].TrimEnd('\\', '/'));
        }
        else
        {
            SimplePanel.Visibility = Visibility.Collapsed;
        }

        foreach (var box in new[] { AddWordBox, ReplaceFromBox, ReplaceToBox, NumberStartBox, NumberPaddingBox })
            box.TextChanged += (_, _) => RefreshPreview();
        AddPositionCombo.SelectionChanged += (_, _) => RefreshPreview();
        DateFormatCombo.SelectionChanged += (_, _) => RefreshPreview();
        DatePositionCombo.SelectionChanged += (_, _) => RefreshPreview();
        RefreshPreview();
    }

    private void RecipeList_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (RecipeList.SelectedIndex < 0) return;
        RecipeHelp.Text = Recipes[RecipeList.SelectedIndex].Help;
        AddWordsPanel.Visibility = RecipeList.SelectedIndex == 0 ? Visibility.Visible : Visibility.Collapsed;
        ReplacePanel.Visibility = RecipeList.SelectedIndex == 1 ? Visibility.Visible : Visibility.Collapsed;
        NumberPanel.Visibility = RecipeList.SelectedIndex == 2 ? Visibility.Visible : Visibility.Collapsed;
        DatePanel.Visibility = RecipeList.SelectedIndex == 3 ? Visibility.Visible : Visibility.Collapsed;
        RefreshPreview();
    }

    private RenameOptions ReadOptions()
    {
        return RecipeList.SelectedIndex switch
        {
            0 => new RenameOptions
            {
                Prefix = (AddPositionCombo.SelectedItem as ComboBoxItem)?.Tag as string == "Beginning" ? AddWordBox.Text : "",
                Suffix = (AddPositionCombo.SelectedItem as ComboBoxItem)?.Tag as string == "End" ? AddWordBox.Text : "",
            },
            1 => new RenameOptions { ReplaceFrom = ReplaceFromBox.Text, ReplaceTo = ReplaceToBox.Text },
            2 => new RenameOptions
            {
                AddNumbering = true,
                NumberStart = int.TryParse(NumberStartBox.Text, out var s) ? s : 1,
                NumberPadding = int.TryParse(NumberPaddingBox.Text, out var p) ? p : 3,
                NumberSeparator = "_",
            },
            3 => new RenameOptions
            {
                AddDate = true,
                DateFormat = (DateFormatCombo.SelectedItem as ComboBoxItem)?.Content?.ToString() ?? "yyyy-MM-dd",
                DatePosition = (DatePositionCombo.SelectedItem as ComboBoxItem)?.Tag as string == "Beginning"
                    ? RenameDatePosition.Beginning : RenameDatePosition.End,
                DateSource = DateSource.Modified,
            },
            4 => new RenameOptions { MakeWebSafe = true },
            5 => new RenameOptions { Case = CaseMode.TitleCase },
            _ => new RenameOptions { ReplaceFrom = "_", ReplaceTo = " " },
        };
    }

    private void RefreshPreview()
    {
        if (RecipeList.SelectedIndex < 0) return;
        _preview = RenameEngine.BuildPreview(_files, ReadOptions());
        Grid.ItemsSource = _preview;
        var conflicts = _preview.Count(p => p.HasConflict);
        var first = _preview.FirstOrDefault();
        SummaryText.Text = $"{_preview.Count} file(s)" +
            (first is not null ? $" · e.g. {first.OldName} → {first.NewName}" : "") +
            (conflicts > 0 ? $" · {conflicts} name(s) already exist — they will be auto-renamed" : "");
    }

    private void SimpleRename_Click(object sender, RoutedEventArgs e)
    {
        if (_files.Count != 1) return;
        var source = _files[0];
        var isDirectory = Directory.Exists(source);
        var oldName = Path.GetFileName(source.TrimEnd('\\', '/'));
        var newName = SimpleNameBox.Text.Trim();
        if (string.IsNullOrEmpty(newName) || newName == oldName) return;
        if (!isDirectory && !newName.Contains('.'))
            newName += Path.GetExtension(source);
        var preview = new RenamePreviewItem
        {
            SourcePath = source, OldName = oldName, NewName = newName,
            NewPath = Path.Combine(Path.GetDirectoryName(source.TrimEnd('\\', '/')) ?? "", newName),
            HasConflict = false,
        };
        var result = RenameEngine.Apply(new[] { preview }, App.State.History);
        if (result.Succeeded == 1)
        {
            Dialogs.Info($"Renamed to {newName}.\n(↶ Undo in the main window reverses this.)");
            DialogResult = true;
            Close();
        }
        else
        {
            Dialogs.Error(result.Files[0].Error ?? "Rename failed.");
        }
    }

    private void Advanced_Click(object sender, RoutedEventArgs e)
    {
        var advanced = new RenameWindow(_files) { Owner = this };
        if (advanced.ShowDialog() == true)
        {
            DialogResult = true;
            Close();
        }
    }

    private void Apply_Click(object sender, RoutedEventArgs e)
    {
        RefreshPreview();
        var conflicts = _preview.Count(p => p.HasConflict);
        if (conflicts > 0 && !Dialogs.Confirm(
                $"{conflicts} new name(s) already exist. Conflicting files will be auto-renamed with \" (1)\" suffixes. Continue?"))
            return;
        var result = RenameEngine.Apply(_preview, App.State.History);
        // Consistent feedback everywhere (overall change): name the failure, don't just count it.
        var firstFail = result.Files.FirstOrDefault(f => !f.Success && !f.Skipped);
        Dialogs.Info($"Renamed {result.Succeeded} file(s)." +
            (result.Failed > 0 ? $"\nFailed: {result.Failed} — {firstFail?.Error}" : "") +
            "\n(↶ Undo in the main window reverses this.)");
        DialogResult = true;
        Close();
    }
}
