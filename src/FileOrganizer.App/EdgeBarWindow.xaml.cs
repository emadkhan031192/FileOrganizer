using System.IO;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media.Animation;
using System.Windows.Threading;
using FileOrganizer.Core;

namespace FileOrganizer.App;

/// <summary>
/// v5 left-edge liquid bar: a 6px glass peek strip lives on the left screen edge. Push the
/// cursor (or drag selected files) to the left edge and the destination panel slides in with
/// a liquid (eased, staggered) motion; it slides back out when the cursor leaves. The strip
/// is a real window, so it works even when File Organizer is not focused — no global hook.
/// </summary>
public partial class EdgeBarWindow : Window
{
    private static EdgeBarWindow? _instance;
    private bool _open;
    private bool _sliding;
    private DispatcherTimer? _hideTimer;

    [DllImport("user32.dll")]
    private static extern bool GetCursorPos(out CursorPoint point);

    [StructLayout(LayoutKind.Sequential)]
    private struct CursorPoint { public int X, Y; }

    private EdgeBarWindow()
    {
        InitializeComponent();
        Top = 0;
        Height = SystemParameters.WorkArea.Height;
        Left = -224; // hidden: only the 6px peek strip is on screen
        RefreshDestinations();
        MouseEnter += (_, _) => SlideIn();
        MouseLeave += (_, _) => StartHideTimer();
        DragEnter += (_, e) =>
        {
            if (e.Data.GetDataPresent(DataFormats.FileDrop)) { e.Effects = DragDropEffects.Move; SlideIn(); }
        };
        DragLeave += (_, _) => StartHideTimer();
    }

    public static void ShowEdgeBar()
    {
        _instance ??= new EdgeBarWindow();
        _instance.RefreshDestinations();
        _instance.Show();
    }

    public static void HideEdgeBar()
    {
        if (_instance is null) return;
        _instance.Hide();
    }

    public static void RefreshIfExists()
    {
        if (App.State.Config.Preferences.EdgeBarEnabled)
            ShowEdgeBar();
        else
            HideEdgeBar();
    }

    private void StartHideTimer()
    {
        _hideTimer?.Stop();
        _hideTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(500) };
        _hideTimer.Tick += (_, _) =>
        {
            _hideTimer?.Stop();
            // Only hide on a real exit — not a stray leave event while the cursor is still on the panel.
            if (GetCursorPos(out var p) && p.X < 230 && p.Y >= Top && p.Y <= Top + Height)
                return;
            SlideOut();
        };
        _hideTimer.Start();
    }

    private void SlideTo(double targetLeft, bool liquid)
    {
        if (_sliding && Math.Abs(Left - targetLeft) < 1) return;
        _sliding = true;
        var anim = new DoubleAnimation(Left, targetLeft, TimeSpan.FromMilliseconds(liquid ? 420 : 300))
        {
            EasingFunction = liquid
                ? new BackEase { Amplitude = 0.35, EasingMode = EasingMode.EaseOut } // liquid overshoot
                : new CubicEase { EasingMode = EasingMode.EaseInOut },
        };
        var sb = new Storyboard();
        Storyboard.SetTarget(anim, this);
        Storyboard.SetTargetProperty(anim, new PropertyPath("Left"));
        sb.Children.Add(anim);
        sb.Completed += (_, _) => _sliding = false;
        sb.Begin();
        _open = targetLeft == 0;
    }

    private void SlideIn() => SlideTo(0, liquid: true);

    private void SlideOut() => SlideTo(-224, liquid: false);

    internal void RefreshDestinations()
    {
        DestinationsPanel.Children.Clear();
        var destinations = App.State.Config.Destinations
            .OrderByDescending(d => d.IsFavorite)
            .ThenByDescending(d => d.LastUsedUtc)
            .ThenBy(d => d.SortOrder)
            .ToList();
        foreach (var dest in destinations)
        {
            var button = new Button
            {
                Style = (Style)FindResource("DestinationButton"),
                Content = $"{dest.Icon} {(dest.IsFavorite ? "★ " : "")}{dest.Name}",
                Tag = dest,
                AllowDrop = true,
                HorizontalAlignment = HorizontalAlignment.Stretch,
                Margin = new Thickness(0, 3, 0, 3),
                ToolTip = dest.ExpandedPath,
            };
            button.Click += Destination_Click;
            button.DragOver += (s, e) => { if (e.Data.GetDataPresent(DataFormats.FileDrop)) e.Effects = DragDropEffects.Move; };
            button.Drop += Destination_Drop;
            DestinationsPanel.Children.Add(button);
        }
        if (App.State.Config.Preferences.RecycleBinLocation is "Bar" or "Both")
        {
            var recycle = new Button
            {
                Style = (Style)FindResource("DestinationButton"),
                Content = "🗑 Recycle Bin",
                AllowDrop = true,
                HorizontalAlignment = HorizontalAlignment.Stretch,
                Margin = new Thickness(0, 3, 0, 3),
                Background = new System.Windows.Media.SolidColorBrush(System.Windows.Media.Color.FromRgb(0xFE, 0xE2, 0xE2)),
            };
            recycle.Click += async (_, _) =>
            {
                var items = ExplorerSelection.GetSelectedFiles();
                if (items.Count == 0) return;
                ToastWindow.Show(await RecycleBinService.RecycleAsync(items));
            };
            recycle.DragOver += (s, e) => { if (e.Data.GetDataPresent(DataFormats.FileDrop)) e.Effects = DragDropEffects.Move; };
            recycle.Drop += async (_, e) =>
            {
                if (e.Data.GetData(DataFormats.FileDrop) is string[] dropped)
                {
                    var items = dropped.Where(p => File.Exists(p) || Directory.Exists(p)).ToList();
                    if (items.Count > 0)
                        ToastWindow.Show(await RecycleBinService.RecycleAsync(items));
                }
            };
            DestinationsPanel.Children.Add(recycle);
        }
    }

    private async void Destination_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not Button { Tag: Destination dest }) return;
        var items = ExplorerSelection.GetSelectedFiles();
        if (items.Count == 0) return;
        await MoveItemsAsync(items, dest);
    }

    private async void Destination_Drop(object sender, DragEventArgs e)
    {
        if (sender is not Button { Tag: Destination dest }) return;
        if (e.Data.GetData(DataFormats.FileDrop) is string[] dropped)
            await MoveItemsAsync(dropped.Where(p => File.Exists(p) || Directory.Exists(p)).ToList(), dest);
    }

    private async Task MoveItemsAsync(List<string> items, Destination dest)
    {
        var mode = App.State.Config.Preferences.DefaultTransferMode;
        var conflict = App.State.Config.Preferences.DefaultConflictPolicy;
        if (!await TransferHelper.ConfirmIfNeededAsync(this, items, dest.Name, mode)) return;
        var result = items.Count >= 10 || items.Any(Directory.Exists)
            ? await TransferHelper.RunTransferWithProgressAsync(this, items, dest, mode, conflict)
            : await Task.Run(() => App.State.Ops.TransferFiles(items, dest.ExpandedPath, mode, conflict));
        if (result.Succeeded > 0)
            App.State.RecordDestinationUsed(dest);
        SlideOut();
        if (Application.Current.MainWindow is MainWindow main)
        {
            main.RefreshActivity();
            main.RefreshDestinations();
            var firstFail = result.Files.FirstOrDefault(f => !f.Success && !f.Skipped);
            ToastWindow.Show(
                $"✓ {result.Succeeded} item(s) → {dest.Name}" +
                (result.Failed > 0 ? $" · {result.Failed} failed: {firstFail?.Error}" : ""),
                onUndo: () => _ = main.UndoLast());
        }
    }

    private async void Organize_Click(object sender, RoutedEventArgs e)
    {
        var folder = ExplorerSelection.GetExplorerFolderPath()
                     ?? Dialogs.PickFolder("Pick the folder to organize");
        if (folder is null) return;
        List<PreviewItem> preview;
        try
        {
            preview = await Task.Run(() => QuickBarWindow.CreateEngine().BuildPreview(folder, App.State.Config.Preferences.IncludeSubfolders));
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
            App.State.Config.Preferences.DefaultTransferMode, App.State.Config.Preferences.DefaultConflictPolicy);
        SlideOut();
        Dialogs.Info($"Organized {result.Succeeded} file(s) in {folder}." +
            (result.Failed > 0 ? $"\nFailed: {result.Failed}" : "") +
            "\n(↶ Undo in the main window reverses this.)");
        if (Application.Current.MainWindow is MainWindow main)
            main.RefreshActivity();
    }

    private void Rename_Click(object sender, RoutedEventArgs e)
    {
        var items = ExplorerSelection.GetSelectedFiles();
        if (items.Count == 0) return;
        new RenameWizardWindow(items).ShowDialog();
    }

    private void Wheel_Click(object sender, RoutedEventArgs e) => WheelWindow.ShowMove();

    private void Settings_Click(object sender, RoutedEventArgs e)
    {
        if (Application.Current.MainWindow is MainWindow main)
        {
            main.Show(); main.Activate();
            new SettingsWindow { Owner = main }.ShowDialog();
            RefreshDestinations();
        }
    }

    private void Hide_Click(object sender, RoutedEventArgs e)
    {
        App.State.Config.Preferences.EdgeBarEnabled = false;
        App.State.Save();
        Hide();
    }
}
