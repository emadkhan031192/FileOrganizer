using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media.Animation;
using System.Windows.Threading;
using FileOrganizer.Core;

namespace FileOrganizer.App;

/// <summary>
/// v4 orb: one truly circular floating button (64px, no rectangular bar) that grows into the
/// 320 destination wheel in the same window, centred on the orb — so moving from the orb to a
/// petal never loses hover. Hover 300ms (or drag files onto it) expands it; leaving for 300ms
/// collapses it. Petals spring out staggered when animations are on; off = instant.
/// Right-click the orb for Open / Settings / Exit orb mode / Hide.
/// </summary>
public partial class OrbWindow : Window
{
    private static OrbWindow? _instance;

    private List<string> _items = new();
    private int _page;
    private bool _expanded;
    private bool _transferring;
    private DispatcherTimer? _hoverTimer;
    private DispatcherTimer? _collapseTimer;
    private Storyboard? _pulse;

    private OrbWindow()
    {
        InitializeComponent();
        var prefs = App.State.Config.Preferences;
        if (prefs.OrbCenterX is not null && prefs.OrbCenterY is not null)
        {
            Left = prefs.OrbCenterX.Value - 32;
            Top = prefs.OrbCenterY.Value - 32;
        }
        else
        {
            Loaded += (_, _) =>
            {
                if (App.State.Config.Preferences.OrbCenterX is null)
                {
                    Left = (SystemParameters.WorkArea.Width - 64) / 2;
                    Top = SystemParameters.WorkArea.Bottom - 64 - 12;
                }
            };
        }

        OrbBorder.MouseEnter += (_, _) => StartHoverTimer();
        OrbBorder.MouseLeave += (_, _) => StopHoverTimer();
        OrbBorder.MouseLeftButtonDown += Orb_DragOrClick;
        OrbBorder.ContextMenu = BuildOrbMenu();

        WheelCanvas.MouseEnter += (_, _) => StopCollapseTimer();
        WheelCanvas.MouseLeave += (_, _) => StartCollapseTimer();
        KeyDown += (_, e) => { if (e.Key == Key.Escape && _expanded) Collapse(); };

        DragEnter += (_, e) =>
        {
            if (e.Data.GetDataPresent(DataFormats.FileDrop)) { e.Effects = DragDropEffects.Move; Expand(); }
        };
        DragLeave += (_, _) => StartCollapseTimer();
        Drop += Window_Drop;

        Loaded += (_, _) => StartPulse();
        Closed += (_, _) => SaveCentre();
    }

    public static void ShowOrb()
    {
        _instance ??= new OrbWindow();
        _instance.Show();
        _instance.StartPulse();
    }

    public static void HideOrb()
    {
        if (_instance is null) return;
        _instance.SaveCentre();
        _instance.Hide();
    }

    /// <summary>Called after Settings saves: orb on when wheel-only mode is on, off otherwise.</summary>
    public static void RefreshIfExists()
    {
        if (App.State.Config.Preferences.WheelOnlyMode)
            ShowOrb();
        else
            HideOrb();
    }

    private ContextMenu BuildOrbMenu()
    {
        var menu = new ContextMenu();
        MenuItem Item(string header, Action action)
        {
            var item = new MenuItem { Header = header };
            item.Click += (_, _) => action();
            menu.Items.Add(item);
            return item;
        }
        Item("🏠 Open File Organizer", () =>
        {
            if (Application.Current.MainWindow is MainWindow main) { main.Show(); main.Activate(); }
        });
        Item("⚙ Open Settings…", () =>
        {
            if (Application.Current.MainWindow is MainWindow main)
            {
                main.Show(); main.Activate();
                new SettingsWindow { Owner = main }.ShowDialog();
                RefreshIfExists();
            }
        });
        Item("◉ Exit orb mode (back to bar)", () =>
        {
            App.State.Config.Preferences.WheelOnlyMode = false;
            App.State.Save();
            HideOrb();
            QuickBarWindow.ShowBar();
        });
        Item("Hide orb", () => Hide());
        return menu;
    }

    private void SaveCentre()
    {
        var prefs = App.State.Config.Preferences;
        prefs.OrbCenterX = Left + Width / 2;
        prefs.OrbCenterY = Top + Height / 2;
        App.State.Save();
    }

    private void StartHoverTimer()
    {
        StopHoverTimer();
        _hoverTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(300) };
        _hoverTimer.Tick += (_, _) => { StopHoverTimer(); Expand(); };
        _hoverTimer.Start();
    }

    private void StopHoverTimer() { _hoverTimer?.Stop(); _hoverTimer = null; }

    private void StartCollapseTimer()
    {
        StopCollapseTimer();
        _collapseTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(300) };
        _collapseTimer.Tick += (_, _) => { StopCollapseTimer(); if (!_transferring) Collapse(); };
        _collapseTimer.Start();
    }

    private void StopCollapseTimer() { _collapseTimer?.Stop(); _collapseTimer = null; }

    private void StartPulse()
    {
        if (!App.State.Config.Preferences.EnableAnimations || _expanded) return;
        StopPulse();
        var scale = (System.Windows.Media.ScaleTransform)OrbBorder.RenderTransform;
        var anim = new DoubleAnimation(1, 1.06, TimeSpan.FromSeconds(1.8))
        {
            AutoReverse = true,
            RepeatBehavior = RepeatBehavior.Forever,
            EasingFunction = new SineEase { EasingMode = EasingMode.EaseInOut },
        };
        _pulse = new Storyboard();
        Storyboard.SetTarget(anim, scale);
        Storyboard.SetTargetProperty(anim, new PropertyPath("ScaleX"));
        var animY = anim.Clone();
        Storyboard.SetTarget(animY, scale);
        Storyboard.SetTargetProperty(animY, new PropertyPath("ScaleY"));
        _pulse.Children.Add(anim);
        _pulse.Children.Add(animY);
        _pulse.Begin();
    }

    private void StopPulse()
    {
        _pulse?.Stop();
        _pulse = null;
        if (OrbBorder.RenderTransform is System.Windows.Media.ScaleTransform s) { s.ScaleX = 1; s.ScaleY = 1; }
    }

    private void Orb_DragOrClick(object sender, MouseButtonEventArgs e)
    {
        var startLeft = Left;
        var startTop = Top;
        try { DragMove(); } catch { /* window not yet shown / drag cancelled */ }
        SaveCentre();
        // A click (no real drag) toggles the wheel; a drag just moves the orb.
        if (Math.Abs(Left - startLeft) < 4 && Math.Abs(Top - startTop) < 4)
        {
            if (_expanded) Collapse(); else Expand();
        }
    }

    private void Expand()
    {
        if (_expanded) return;
        StopPulse();
        var cx = Left + Width / 2;
        var cy = Top + Height / 2;
        var wa = SystemParameters.WorkArea;
        Width = 320; Height = 320;
        Left = Math.Clamp(cx - 160, wa.Left, wa.Right - 320);
        Top = Math.Clamp(cy - 160, wa.Top, wa.Bottom - 320);
        _items = ExplorerSelection.GetSelectedFiles();
        OrbBorder.Visibility = Visibility.Collapsed;
        WheelCanvas.Visibility = Visibility.Visible;
        _expanded = true;
        BuildWheel();
    }

    private void Collapse()
    {
        if (!_expanded) return;
        var cx = Left + Width / 2;
        var cy = Top + Height / 2;
        WheelCanvas.Visibility = Visibility.Collapsed;
        OrbBorder.Visibility = Visibility.Visible;
        Width = 64; Height = 64;
        Left = cx - 32;
        Top = cy - 32;
        _expanded = false;
        SaveCentre();
        StartPulse();
    }

    private string DefaultCentreText => _items.Count == 0
        ? "Nothing selected\nSelect files in Explorer,\nor drop files here"
        : $"{_items.Count} item(s) ready";

    private void BuildWheel()
    {
        foreach (var child in WheelCanvas.Children.OfType<Button>().ToList())
            WheelCanvas.Children.Remove(child);
        CenterText.Text = DefaultCentreText;

        var destinations = App.State.Config.Destinations
            .OrderByDescending(d => d.IsFavorite)
            .ThenByDescending(d => d.LastUsedUtc)
            .ThenBy(d => d.SortOrder)
            .ToList();
        var showRecycle = App.State.Config.Preferences.RecycleBinLocation is "Wheel" or "Both";

        const int pageSize = 8;
        var pages = Math.Max(1, (destinations.Count + pageSize - 1) / pageSize);
        _page %= pages;
        var pageDests = destinations.Skip(_page * pageSize).Take(pageSize).ToList();
        var hasMore = pages > 1;
        var shown = hasMore ? pageDests.Take(pageSize - 1).ToList() : pageDests;

        // Slots: destinations (+ recycle petal, last) (+ ⋯ More when paging).
        var slots = shown.Count + (showRecycle ? 1 : 0) + (hasMore ? 1 : 0);
        if (slots == 0) return;
        const double cx = 160, cy = 160, radius = 112, w = 64, h = 64;
        var index = 0;

        Button MakePetal(string icon, string toolTip, string? colorHex)
        {
            var button = new Button
            {
                Style = (Style)FindResource("OrbPetalButton"),
                Content = icon,
                ToolTip = toolTip,
                AllowDrop = true,
                RenderTransformOrigin = new Point(0.5, 0.5),
                RenderTransform = new System.Windows.Media.ScaleTransform(0, 0),
            };
            button.DragOver += (_, e) => { if (e.Data.GetDataPresent(DataFormats.FileDrop)) e.Effects = DragDropEffects.Move; };
            if (!string.IsNullOrWhiteSpace(colorHex) &&
                System.Windows.Media.ColorConverter.ConvertFromString(colorHex) is System.Windows.Media.Color color)
                button.Background = new System.Windows.Media.SolidColorBrush(color);
            var angle = -Math.PI / 2 + index * 2 * Math.PI / slots;
            Canvas.SetLeft(button, cx + radius * Math.Cos(angle) - w / 2);
            Canvas.SetTop(button, cy + radius * Math.Sin(angle) - h / 2);
            WheelCanvas.Children.Add(button);
            index++;
            return button;
        }

        foreach (var dest in shown)
        {
            var button = MakePetal(dest.Icon, $"{(dest.IsFavorite ? "★ " : "")}{dest.Name} — {dest.ExpandedPath}", dest.ColorHex);
            button.Tag = dest;
            button.Click += Petal_Click;
            button.MouseEnter += (_, _) => CenterText.Text = (dest.IsFavorite ? "★ " : "") + dest.Name;
            button.MouseLeave += (_, _) => CenterText.Text = DefaultCentreText;
            button.Drop += Petal_Drop;
        }

        if (showRecycle)
        {
            var recycle = MakePetal("🗑", "Recycle Bin — sends files to the Windows Recycle Bin", "#DC2626");
            recycle.Foreground = System.Windows.Media.Brushes.White;
            recycle.Tag = "recycle";
            recycle.Click += Recycle_Click;
            recycle.MouseEnter += (_, _) => CenterText.Text = "🗑 Recycle Bin";
            recycle.MouseLeave += (_, _) => CenterText.Text = DefaultCentreText;
            recycle.Drop += Recycle_Drop;
        }

        if (hasMore)
        {
            var more = MakePetal("⋯", $"More destinations (page {_page + 1} of {pages})", null);
            more.Tag = "more";
            more.Click += (_, _) => { _page = (_page + 1) % pages; BuildWheel(); };
        }

        // Catchy Mac-style animation: petals spring out staggered from the centre.
        var petals = WheelCanvas.Children.OfType<Button>().ToList();
        if (App.State.Config.Preferences.EnableAnimations)
        {
            for (var i = 0; i < petals.Count; i++)
            {
                var scale = (System.Windows.Media.ScaleTransform)petals[i].RenderTransform;
                var sb = new Storyboard();
                foreach (var prop in new[] { "ScaleX", "ScaleY" })
                {
                    var anim = new DoubleAnimation(0, 1, TimeSpan.FromMilliseconds(260))
                    {
                        BeginTime = TimeSpan.FromMilliseconds(i * 40),
                        EasingFunction = new BackEase { Amplitude = 0.6, EasingMode = EasingMode.EaseOut },
                    };
                    Storyboard.SetTarget(anim, scale);
                    Storyboard.SetTargetProperty(anim, new PropertyPath(prop));
                    sb.Children.Add(anim);
                }
                sb.Begin();
            }
        }
        else
        {
            foreach (var petal in petals)
                if (petal.RenderTransform is System.Windows.Media.ScaleTransform s) { s.ScaleX = 1; s.ScaleY = 1; }
        }
    }

    private void Window_Drop(object sender, DragEventArgs e)
    {
        if (e.Data.GetData(DataFormats.FileDrop) is string[] dropped)
        {
            _items = dropped.Where(p => File.Exists(p) || Directory.Exists(p)).ToList();
            if (!_expanded) Expand(); else BuildWheel();
        }
    }

    private async void Petal_Drop(object sender, DragEventArgs e)
    {
        if (sender is not Button { Tag: Destination dest }) return;
        if (e.Data.GetData(DataFormats.FileDrop) is string[] dropped)
            await MoveItemsAsync(dropped.Where(p => File.Exists(p) || Directory.Exists(p)).ToList(), dest);
    }

    private async void Petal_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not Button { Tag: Destination dest }) return;
        if (_items.Count == 0)
        {
            _items = ExplorerSelection.GetSelectedFiles();
            if (_items.Count == 0) { BuildWheel(); return; }
        }
        await MoveItemsAsync(_items, dest);
    }

    private async Task MoveItemsAsync(List<string> items, Destination dest)
    {
        _transferring = true;
        try
        {
            var mode = App.State.Config.Preferences.DefaultTransferMode;
            var conflict = App.State.Config.Preferences.DefaultConflictPolicy;
            if (!await TransferHelper.ConfirmIfNeededAsync(this, items, dest.Name, mode)) return;
            var result = items.Count >= 10 || items.Any(Directory.Exists)
                ? await TransferHelper.RunTransferWithProgressAsync(this, items, dest, mode, conflict)
                : await Task.Run(() => App.State.Ops.TransferFiles(items, dest.ExpandedPath, mode, conflict));
            if (result.Succeeded > 0)
                App.State.RecordDestinationUsed(dest);
            Collapse();
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

    private async void Recycle_Drop(object sender, DragEventArgs e)
    {
        if (e.Data.GetData(DataFormats.FileDrop) is string[] dropped)
            await RecycleItemsAsync(dropped.Where(p => File.Exists(p) || Directory.Exists(p)).ToList());
    }

    private async void Recycle_Click(object sender, RoutedEventArgs e)
    {
        var items = _items.Count > 0 ? _items : ExplorerSelection.GetSelectedFiles();
        if (items.Count == 0) { BuildWheel(); return; }
        await RecycleItemsAsync(items);
    }

    private async Task RecycleItemsAsync(List<string> items)
    {
        _transferring = true;
        try
        {
            var message = await RecycleBinService.RecycleAsync(items);
            Collapse();
            ToastWindow.Show(message); // no Undo: restore from the Windows Recycle Bin itself
            if (Application.Current.MainWindow is MainWindow main)
                main.RefreshActivity();
        }
        finally
        {
            _transferring = false;
        }
    }
}
