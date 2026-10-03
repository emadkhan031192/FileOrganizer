using System.Windows;
using System.Windows.Controls;
using FileOrganizer.Core;

namespace FileOrganizer.App;

public partial class RenameWindow : Window
{
    private readonly List<string> _files;
    private List<RenamePreviewItem> _preview = new();

    public RenameWindow(List<string> files)
    {
        InitializeComponent();
        _files = files;
        Title = $"Batch Rename — {files.Count} file(s)";
        RefreshPreview();
    }

    private RenameOptions ReadOptions() => new()
    {
        ReplaceFrom = ReplaceFromBox.Text,
        ReplaceTo = ReplaceToBox.Text,
        RemoveText = RemoveTextBox.Text,
        Case = (CaseCombo.SelectedItem as ComboBoxItem)?.Tag is string tag && Enum.TryParse<CaseMode>(tag, out var cm) ? cm : CaseMode.None,
        Prefix = PrefixBox.Text,
        Suffix = SuffixBox.Text,
        AddDate = AddDateCheck.IsChecked == true,
        DateFormat = (DateFormatCombo.SelectedItem as ComboBoxItem)?.Content?.ToString() ?? "yyyy-MM-dd",
        DatePosition = (DatePositionCombo.SelectedItem as ComboBoxItem)?.Tag as string == "Beginning"
            ? RenameDatePosition.Beginning : RenameDatePosition.End,
        DateSource = DateSource.Modified,
        AddNumbering = AddNumberingCheck.IsChecked == true,
        NumberStart = int.TryParse(NumberStartBox.Text, out var start) ? start : 1,
        NumberPadding = int.TryParse(NumberPaddingBox.Text, out var pad) ? pad : 3,
    };

    private void RefreshPreview()
    {
        _preview = RenameEngine.BuildPreview(_files, ReadOptions());
        Grid.ItemsSource = _preview;
    }

    private void Preview_Click(object sender, RoutedEventArgs e) => RefreshPreview();

    private void Apply_Click(object sender, RoutedEventArgs e)
    {
        RefreshPreview();
        var conflicts = _preview.Count(p => p.HasConflict);
        if (conflicts > 0 && !Dialogs.Confirm(
                $"{conflicts} new name(s) already exist. Conflicting files will be auto-renamed with \" (1)\" suffixes. Continue?"))
            return;

        var result = RenameEngine.Apply(_preview, App.State.History);
        Dialogs.Info($"Renamed {result.Succeeded} file(s)." + (result.Failed > 0 ? $"\nFailed: {result.Failed}" : "") +
                     "\n(↶ Undo in the main window reverses this.)");
        DialogResult = true;
        Close();
    }
}
