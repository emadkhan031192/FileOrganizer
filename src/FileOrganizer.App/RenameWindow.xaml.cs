using System.IO;
using System.Windows;
using System.Windows.Controls;
using FileOrganizer.Core;

namespace FileOrganizer.App;

public partial class RenameWindow : Window
{
    private readonly List<string> _files;
    private List<RenamePreviewItem> _preview = new();
    private readonly List<RenamePreset> _presets;

    public RenameWindow(List<string> files)
    {
        InitializeComponent();
        _files = files;
        Title = $"Batch Rename — {files.Count} file(s)/folder(s)";
        // Existing installs created before presets existed get the built-in set.
        _presets = App.State.Config.RenamePresets.Count > 0
            ? App.State.Config.RenamePresets
            : AppConfig.CreateDefault().RenamePresets;
        PresetCombo.ItemsSource = _presets.Select(p => p.Name).ToList();
        if (_presets.Count > 0)
            PresetCombo.SelectedIndex = 0;
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
        MakeWebSafe = _webSafe,
    };

    private bool _webSafe;

    private void ApplyOptionsToControls(RenameOptions o)
    {
        ReplaceFromBox.Text = o.ReplaceFrom;
        ReplaceToBox.Text = o.ReplaceTo;
        RemoveTextBox.Text = o.RemoveText;
        PrefixBox.Text = o.Prefix;
        SuffixBox.Text = o.Suffix;
        _webSafe = o.MakeWebSafe;
        foreach (ComboBoxItem item in CaseCombo.Items)
            if (item.Tag as string == o.Case.ToString())
                CaseCombo.SelectedItem = item;
        AddDateCheck.IsChecked = o.AddDate;
        foreach (ComboBoxItem item in DateFormatCombo.Items)
            if (item.Content as string == o.DateFormat)
                DateFormatCombo.SelectedItem = item;
        AddNumberingCheck.IsChecked = o.AddNumbering;
        NumberStartBox.Text = o.NumberStart.ToString();
        NumberPaddingBox.Text = o.NumberPadding.ToString();
    }

    private void ApplyPreset_Click(object sender, RoutedEventArgs e)
    {
        if (PresetCombo.SelectedIndex < 0 || PresetCombo.SelectedIndex >= _presets.Count)
            return;
        ApplyOptionsToControls(_presets[PresetCombo.SelectedIndex].Options);
        RefreshPreview();
    }

    private void SavePreset_Click(object sender, RoutedEventArgs e)
    {
        var name = Dialogs.Prompt("Save preset", "Preset name:", "My Preset");
        if (name is null) return;
        App.State.Config.RenamePresets.RemoveAll(p => p.Name == name);
        App.State.Config.RenamePresets.Add(new RenamePreset { Name = name, Options = ReadOptions() });
        App.State.Save();
        _presets.Clear();
        _presets.AddRange(App.State.Config.RenamePresets);
        PresetCombo.ItemsSource = _presets.Select(p => p.Name).ToList();
        PresetCombo.SelectedItem = name;
    }

    /// <summary>✎ Simple Rename (from the reference menu): type one new name for one item. Keeps its extension unless you type one.</summary>
    private void SimpleRename_Click(object sender, RoutedEventArgs e)
    {
        if (_files.Count != 1)
        {
            Dialogs.Info("Simple Rename works on a single file/folder. Select just one item, or use the batch tools below.");
            return;
        }
        var source = _files[0];
        var isDirectory = Directory.Exists(source);
        var oldName = Path.GetFileName(source.TrimEnd('\\', '/'));
        var newName = Dialogs.Prompt("Simple Rename", "New name:", oldName);
        if (newName is null || newName == oldName) return;
        if (!isDirectory && !newName.Contains('.'))
            newName += Path.GetExtension(source); // typed "Poster" keeps ".jpg"

        var preview = new RenamePreviewItem
        {
            SourcePath = source,
            OldName = oldName,
            NewName = newName,
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
