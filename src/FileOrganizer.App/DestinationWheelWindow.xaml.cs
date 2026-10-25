using System.IO;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using FileOrganizer.Core;

namespace FileOrganizer.App;

/// <summary>
/// v3 small Mac-style wheel: 320×320, opens near the cursor (not centre-screen), icon-only
/// circular petals, destination name shown in the centre on hover, max 8 petals per page
/// (a "⋯" petal pages through the rest). Esc or clicking away closes it.
/// Opens with Explorer's current selection, or with files dropped onto it / the main window.
/// </summary>
public partial class DestinationWheelWindow : Window
{
    private List<string> _items;
    private int _page;
    private bool _transferring;

    [DllImport("user32.dll")]
    private static extern bool GetCursorPos(out Point point);

    [StructLayout(LayoutKind.Sequential)]
    private struct Point { public int X, Y; }

    public DestinationWheelWindow(List<string>? items = null)
    {
        InitializeComponent();
        _items = items ?? ExplorerSelection.GetSelectedFiles();
        Loaded += (_, _) =>
        {
            // Near the cursor, clamped inside the work area.
            if (GetCursorPos(out var p))
            {
                var wa = SystemParameters.WorkArea;
                Left = Math.Clamp(p.X - 160, wa.Left, wa.Right - 320);
                Top = Math.Clamp(p.Y - 160, wa.Top, wa.Bottom - 320);
            }
            BuildWheel();
        };
        KeyDown += (_, e) => { if (e.Key == Key.Escape) Close(); };
        Deactivated += (_, _) => { if (!_transferring) Close(); };
        DragOver += (_, e) => { if (e.Data.GetDataPresent(DataFormats.FileDrop)) e.Effects = DragDropEffects.Move; };
        Drop += Wheel_Drop;
    }

    public static void ShowWheel(List<string>? items = null) => new DestinationWheelWindow(items).ShowDialog();

    private string DefaultCentreText => _items.Count == 0
        ? "Nothing selected\nSelect files in Explorer,\nor drop files here"
        : $"{_items.Count} item(s) ready";

    private void BuildWheel()
    {
        foreach (var child in WheelCanvas.Children.OfType<Button>().Where(b => b.Tag is Destination or string).ToList())
            WheelCanvas.Children.Remove(child);
        CenterText.Text = DefaultCentreText;

        var destinations = App.State.Config.Destinations
            .OrderByDescending(d => d.IsFavorite)
            .ThenByDescending(d => d.LastUsedUtc)
            .ThenBy(d => d.SortOrder)
            .ToList();
        if (destinations.Count == 0) return;

        const int pageSize = 8;
        var pages = (destinations.Count + pageSize - 1) / pageSize;
        _page %= pages;
        var pageDests = destinations.Skip(_page * pageSize).Take(pageSize).ToList();
        var hasMore = pages > 1;
        // With paging, reserve the last slot for "⋯ More".
        var shown = hasMore ? pageDests.Take(pageSize - 1).ToList() : pageDests;
        var showRecycle = App.State.Config.Preferences.RecycleBinLocation is "Wheel" or "Both";
        var slots = shown.Count + (hasMore ? 1 : 0) + (showRecycle ? 1 : 0);

        const double cx = 160, cy = 160, radius = 112, w = 64, h = 64;
        for (var i = 0; i < shown.Count; i++)
        {
            var dest = shown[i];
            var angle = -Math.PI / 2 + i * 2 * Math.PI / slots;
            var button = new Button
            {
                Style = (Style)FindResource("PetalButton"),
                Tag = dest,
                ToolTip = $"{(dest.IsFavorite ? "★ " : "")}{dest.Name} — {dest.ExpandedPath}",
                Content = dest.Icon,
            };
            if (!string.IsNullOrWhiteSpace(dest.ColorHex) &&
                System.Windows.Media.ColorConverter.ConvertFromString(dest.ColorHex) is System.Windows.Media.Color color)
                button.Background = new System.Windows.Media.SolidColorBrush(color);
            button.Click += Petal_Click;
            button.MouseEnter += (_, _) => CenterText.Text = (dest.IsFavorite ? "★ " : "") + dest.Name;
            button.MouseLeave += (_, _) => CenterText.Text = DefaultCentreText;
            Canvas.SetLeft(button, cx + radius * Math.Cos(angle) - w / 2);
            Canvas.SetTop(button, cy + radius * Math.Sin(angle) - h / 2);
            WheelCanvas.Children.Add(button);
        }

        if (showRecycle)
        {
            var angle = -Math.PI / 2 + shown.Count * 2 * Math.PI / slots;
            var recycle = new Button
            {
                Style = (Style)FindResource("PetalButton"),
                Tag = "recycle",
                Content = "🗑",
                ToolTip = "Recycle Bin — sends files to the Windows Recycle Bin",
                Background = new System.Windows.Media.SolidColorBrush(System.Windows.Media.Color.FromRgb(0xDC, 0x26, 0x26)),
                Foreground = System.Windows.Media.Brushes.White,
            };
            recycle.Click += Recycle_Click;
            recycle.MouseEnter += (_, _) => CenterText.Text = "🗑 Recycle Bin";
            recycle.MouseLeave += (_, _) => CenterText.Text = DefaultCentreText;
            Canvas.SetLeft(recycle, cx + radius * Math.Cos(angle) - w / 2);
            Canvas.SetTop(recycle, cy + radius * Math.Sin(angle) - h / 2);
            WheelCanvas.Children.Add(recycle);
        }

        if (hasMore)
        {
            var angle = -Math.PI / 2 + (shown.Count + (showRecycle ? 1 : 0)) * 2 * Math.PI / slots;
            var more = new Button
            {
                Style = (Style)FindResource("PetalButton"),
                Tag = "more",
                Content = "⋯",
                ToolTip = $"More destinations (page {_page + 1} of {pages})",
            };
            more.Click += (_, _) => { _page = (_page + 1) % pages; BuildWheel(); };
            Canvas.SetLeft(more, cx + radius * Math.Cos(angle) - w / 2);
            Canvas.SetTop(more, cy + radius * Math.Sin(angle) - h / 2);
            WheelCanvas.Children.Add(more);
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
        _transferring = true;
        try
        {
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
        finally
        {
            _transferring = false;
        }
    }

    private async void Recycle_Click(object sender, RoutedEventArgs e)
    {
        var items = _items.Count > 0 ? _items : ExplorerSelection.GetSelectedFiles();
        if (items.Count == 0) { BuildWheel(); return; }
        var message = await RecycleBinService.RecycleAsync(items);
        Close();
        ToastWindow.Show(message);
        if (Application.Current.MainWindow is MainWindow main)
            main.RefreshActivity();
    }

    private void Close_Click(object sender, RoutedEventArgs e) => Close();
}
