using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using FileOrganizer.Core;

namespace FileOrganizer.App;

/// <summary>
/// v2 (recommendation 1): the Tangerine-style destination wheel. Destinations sit as petals
/// around the centre — favourites first, then most recently used — so one drop/click moves
/// files without hunting for a button. Hover glow comes from the DestinationButton style.
/// Opens with Explorer's current selection, or with files dropped onto it / the main window.
/// </summary>
public partial class DestinationWheelWindow : Window
{
    private List<string> _items;

    public DestinationWheelWindow(List<string>? items = null)
    {
        InitializeComponent();
        _items = items ?? ExplorerSelection.GetSelectedFiles();
        Loaded += (_, _) => BuildWheel();
        KeyDown += (_, e) => { if (e.Key == Key.Escape) Close(); };
        DragOver += (_, e) => { if (e.Data.GetDataPresent(DataFormats.FileDrop)) e.Effects = DragDropEffects.Move; };
        Drop += Wheel_Drop;
    }

    public static void ShowWheel(List<string>? items = null) => new DestinationWheelWindow(items).ShowDialog();

    private void BuildWheel()
    {
        // Remove old petals (keep the centre text, hint and close button).
        foreach (var child in WheelCanvas.Children.OfType<Button>().Where(b => b.Tag is Destination).ToList())
            WheelCanvas.Children.Remove(child);

        CenterText.Text = _items.Count == 0
            ? "Nothing selected\nSelect files in Explorer,\nor drop files here"
            : $"{_items.Count} item(s) ready\nDrop or click a destination";

        var destinations = App.State.Config.Destinations
            .OrderByDescending(d => d.IsFavorite)
            .ThenByDescending(d => d.LastUsedUtc)
            .ThenBy(d => d.SortOrder)
            .ToList();
        if (destinations.Count == 0) return;

        const double cx = 280, cy = 280, radius = 185, w = 122, h = 66;
        for (var i = 0; i < destinations.Count; i++)
        {
            var dest = destinations[i];
            var angle = -Math.PI / 2 + i * 2 * Math.PI / destinations.Count;
            var button = new Button
            {
                Style = (Style)FindResource("DestinationButton"),
                Width = w, Height = h, Tag = dest,
                ToolTip = $"{(dest.IsFavorite ? "★ Favourite · " : "")}{dest.ExpandedPath}",
                Content = new StackPanel
                {
                    Children =
                    {
                        new TextBlock { Text = dest.Icon, FontSize = 18, TextAlignment = TextAlignment.Center },
                        new TextBlock { Text = (dest.IsFavorite ? "★ " : "") + dest.Name, TextAlignment = TextAlignment.Center, TextWrapping = TextWrapping.Wrap, FontSize = 11 },
                    },
                },
            };
            if (!string.IsNullOrWhiteSpace(dest.ColorHex) &&
                System.Windows.Media.ColorConverter.ConvertFromString(dest.ColorHex) is System.Windows.Media.Color color)
                button.Background = new System.Windows.Media.SolidColorBrush(color);
            button.Click += Petal_Click;
            Canvas.SetLeft(button, cx + radius * Math.Cos(angle) - w / 2);
            Canvas.SetTop(button, cy + radius * Math.Sin(angle) - h / 2);
            WheelCanvas.Children.Add(button);
        }
    }

    private void Wheel_Drop(object sender, DragEventArgs e)
    {
        if (e.Data.GetData(DataFormats.FileDrop) is string[] dropped)
        {
            _items = dropped.Where(p => File.Exists(p) || Directory.Exists(p)).ToList();
            BuildWheel();
        }
    }

    private async void Petal_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not Button { Tag: Destination dest }) return;
        if (_items.Count == 0)
        {
            _items = ExplorerSelection.GetSelectedFiles();
            if (_items.Count == 0) { BuildWheel(); return; }
        }
        var mode = App.State.Config.Preferences.DefaultTransferMode;
        var conflict = App.State.Config.Preferences.DefaultConflictPolicy;
        if (!await TransferHelper.ConfirmIfNeededAsync(this, _items, dest.Name, mode)) return;
        var result = _items.Count >= 10 || _items.Any(Directory.Exists)
            ? await TransferHelper.RunTransferWithProgressAsync(this, _items, dest, mode, conflict)
            : await Task.Run(() => App.State.Ops.TransferFiles(_items, dest.ExpandedPath, mode, conflict));
        if (result.Succeeded > 0)
            App.State.RecordDestinationUsed(dest);
        Close();
        if (Application.Current.MainWindow is MainWindow main)
        {
            main.RefreshActivity();
            var firstFail = result.Files.FirstOrDefault(f => !f.Success && !f.Skipped);
            ToastWindow.Show(
                $"✓ {result.Succeeded} item(s) → {dest.Name}" +
                (result.Failed > 0 ? $" · {result.Failed} failed: {firstFail?.Error}" : ""),
                onUndo: () => _ = main.UndoLast());
        }
    }

    private void Close_Click(object sender, RoutedEventArgs e) => Close();
}
