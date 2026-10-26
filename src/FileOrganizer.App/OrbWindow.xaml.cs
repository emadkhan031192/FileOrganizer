using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media.Animation;
using System.Windows.Threading;
using FileOrganizer.App.Glass;

namespace FileOrganizer.App;

/// <summary>
/// Phase 2 Orb layout: one idle floating glass orb (64px circle). It does not
/// contain its own wheel any more — the unified WheelWindow owns all wheels.
/// Hover 300 ms, click, touch-hold, or dropping files on the orb opens the
/// Move wheel centred on the orb. Dragging the orb moves it; its position is
/// persisted in Preferences.OrbCenterX/Y. Right-click: Open / Settings / Exit
/// orb mode / Hide. Idle motion: the glow fades 0.25 to 0.55 over 1.8 s (sine,
/// reverse) plus a light sweep across the surface every ~6 s; the glass visual
/// scales to 1.06 on hover while the hit area stays the same size.
/// </summary>
public partial class OrbWindow : Window
{
    private static OrbWindow? _instance;
    private DispatcherTimer? _hoverTimer;
    private DispatcherTimer? _sweepTimer;
    private Storyboard? _pulse;
    private bool _draggingOrb;

    private OrbWindow()
    {
        InitializeComponent();
        var prefs = App.State.Config.Preferences;
        if (prefs.OrbCenterX is not null && prefs.OrbCenterY is not null)
        {
            Left = prefs.OrbCenterX.Value - 40;
            Top = prefs.OrbCenterY.Value - 40;
        }
        else
        {
            var wa = SystemParameters.WorkArea;
            Left = wa.Right - 110;
            Top = wa.Top + 40;
        }
        Loaded += (_, _) => { StartPulse(); StartSweepTimer(); };
        Closed += (_, _) =>
        {
            if (ReferenceEquals(_instance, this)) _instance = null;
            _hoverTimer?.Stop();
            _sweepTimer?.Stop();
            _pulse?.Stop();
        };
        OrbBorder.MouseRightButtonUp += (_, _) => ShowContextMenu();
        OrbBorder.TouchDown += (_, _) => StartHoverTimer();
        OrbBorder.TouchUp += (_, _) => _hoverTimer?.Stop();
    }

    public static void ShowOrb()
    {
        _instance ??= new OrbWindow();
        _instance.Show();
    }

    public static void HideOrb()
    {
        _instance?.Hide();
    }

    private void StartHoverTimer()
    {
        if (_draggingOrb) return;
        _hoverTimer?.Stop();
        _hoverTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(300) };
        _hoverTimer.Tick += (_, _) => { _hoverTimer?.Stop(); OpenWheel(); };
        _hoverTimer.Start();
    }

    private void OpenWheel()
    {
        var centre = new Point(Left + Width / 2, Top + Height / 2);
        var items = ExplorerSelection.GetSelectedFiles();
        WheelWindow.Show(WheelWindow.WheelKind.Move, items, fromDrag: false, anchor: centre);
    }

    private void Orb_MouseEnter(object sender, MouseEventArgs e)
    {
        StartHoverTimer();
        // Hover: the glass visual scales to 1.06; the hit area (the Border
        // itself) does not change, because only the visual transform moves.
        SpringAnimator.AnimateScale(OrbBorder, 1.06, SpringAnimator.SpringPreset.Snappy);
    }

    private void Orb_MouseLeave(object sender, MouseEventArgs e)
    {
        _hoverTimer?.Stop();
        SpringAnimator.AnimateScale(OrbBorder, 1.0, SpringAnimator.SpringPreset.Snappy);
    }

    private void Orb_DragOrClick(object sender, MouseButtonEventArgs e)
    {
        _draggingOrb = true;
        _hoverTimer?.Stop();
        var startLeft = Left;
        var startTop = Top;
        try { DragMove(); } catch { /* drag cancelled */ }
        _draggingOrb = false;
        SaveCentre();
        if (Math.Abs(Left - startLeft) < 4 && Math.Abs(Top - startTop) < 4)
            OpenWheel();
    }

    private void Orb_DragOver(object sender, DragEventArgs e)
    {
        if (e.Data.GetDataPresent(DataFormats.FileDrop))
        {
            e.Effects = DragDropEffects.Move;
            e.Handled = true;
        }
    }

    private void Orb_Drop(object sender, DragEventArgs e)
    {
        if (e.Data.GetData(DataFormats.FileDrop) is string[] dropped)
        {
            var items = dropped.Where(p => File.Exists(p) || Directory.Exists(p)).ToList();
            if (items.Count == 0) return;
            var centre = new Point(Left + Width / 2, Top + Height / 2);
            WheelWindow.Show(WheelWindow.WheelKind.Move, items, fromDrag: false, anchor: centre);
        }
    }

    private void SaveCentre()
    {
        var prefs = App.State.Config.Preferences;
        prefs.OrbCenterX = Left + Width / 2;
        prefs.OrbCenterY = Top + Height / 2;
        App.State.Save();
    }

    private void ShowContextMenu()
    {
        var menu = new ContextMenu();
        var open = new MenuItem { Header = "Open File Organizer" };
        open.Click += (_, _) =>
        {
            if (Application.Current.MainWindow is MainWindow main) { main.Show(); main.Activate(); }
        };
        var settings = new MenuItem { Header = "Settings" };
        settings.Click += (_, _) =>
        {
            if (Application.Current.MainWindow is MainWindow main)
                new SettingsWindow { Owner = main }.ShowDialog();
        };
        var exit = new MenuItem { Header = "Exit orb mode" };
        exit.Click += (_, _) =>
        {
            App.State.Config.Preferences.WheelOnlyMode = false;
            App.State.Save();
            Hide();
            QuickBarWindow.RefreshIfExists();
        };
        var hide = new MenuItem { Header = "Hide" };
        hide.Click += (_, _) => Hide();
        menu.Items.Add(open);
        menu.Items.Add(settings);
        menu.Items.Add(exit);
        menu.Items.Add(hide);
        menu.PlacementTarget = OrbBorder;
        menu.IsOpen = true;
    }

    private void StartPulse()
    {
        if (SpringAnimator.ReduceMotion) return;
        _pulse?.Stop();
        var anim = new DoubleAnimation(0.25, 0.55, TimeSpan.FromSeconds(1.8))
        {
            AutoReverse = true,
            RepeatBehavior = RepeatBehavior.Forever,
            EasingFunction = new SineEase { EasingMode = EasingMode.EaseInOut },
        };
        _pulse = new Storyboard();
        Storyboard.SetTarget(anim, GlowBorder);
        Storyboard.SetTargetProperty(anim, new PropertyPath("Opacity"));
        _pulse.Children.Add(anim);
        _pulse.Begin();
    }

    private void StartSweepTimer()
    {
        _sweepTimer?.Stop();
        _sweepTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(6) };
        _sweepTimer.Tick += (_, _) =>
        {
            if (SpringAnimator.ReduceMotion) return;
            var translate = (System.Windows.Media.TranslateTransform)SweepBorder.RenderTransform;
            translate.X = -64;
            SweepBorder.Opacity = 0.5;
            var anim = new DoubleAnimation(-64, 64, TimeSpan.FromMilliseconds(800))
            {
                EasingFunction = new CubicEase { EasingMode = EasingMode.EaseInOut },
            };
            anim.Completed += (_, _) => SweepBorder.Opacity = 0;
            translate.BeginAnimation(System.Windows.Media.TranslateTransform.XProperty, anim);
        };
        _sweepTimer.Start();
    }
}
