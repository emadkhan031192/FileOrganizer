using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using FileOrganizer.App.Glass;

namespace FileOrganizer.App;

/// <summary>
/// Phase 1 gallery: a live preview of every glass component. It is not a
/// product screen — it exists so the design system can be checked on a real
/// Windows machine before Phases 2-5 build on it. Open it from the main
/// window's Gallery button, or with FileOrganizer.exe --gallery.
/// </summary>
public partial class GalleryWindow : Window
{
    private string _themePreference = "Light";

    public GalleryWindow()
    {
        InitializeComponent();
        Loaded += (_, _) =>
        {
            // Real DWM backdrop on Windows 11 22H2+; fake-glass brushes otherwise.
            var dark = BackdropHelper.ResolveTheme(_themePreference) == "Dark";
            if (!BackdropHelper.TryApplyBackdrop(this, BackdropHelper.BackdropKind.Mica, dark))
                Background = (Brush)FindResource("GlassSurface");
            BuildIconGrid();
            ApplyAppearance();
        };
    }

    private void BuildIconGrid()
    {
        var icons = new (string Name, string Key)[]
        {
            ("Folder", "IconFolder"), ("File", "IconFile"), ("Move", "IconMove"),
            ("Organize", "IconOrganize"), ("Rename", "IconRename"), ("Search", "IconSearch"),
            ("Duplicates", "IconDuplicates"), ("Settings", "IconSettings"), ("Recycle", "IconRecycle"),
            ("Terminal", "IconTerminal"), ("Reveal", "IconReveal"), ("Pin", "IconPin"),
            ("Copy", "IconCopy"), ("Check", "IconCheck"), ("Close", "IconClose"),
            ("Chevron", "IconChevronRight"), ("Clock", "IconClock"), ("Sparkle", "IconSparkle"),
            ("Wheel", "IconWheel"), ("Orb", "IconOrb"), ("Edge bar", "IconEdgeBar"),
            ("Bar", "IconBar"), ("Undo", "IconUndo"), ("Plus", "IconPlus"),
            ("Download", "IconDownload"), ("Filter", "IconFilter"), ("Tools", "IconTools"),
        };
        foreach (var (name, key) in icons)
        {
            if (FindResource(key) is not Geometry geometry) continue;
            var panel = new StackPanel { Width = 72, Margin = new Thickness(4), HorizontalAlignment = HorizontalAlignment.Center };
            var path = new System.Windows.Shapes.Path
            {
                Data = geometry,
                Fill = (Brush)FindResource("GlassTextPrimary"),
                Width = 24,
                Height = 24,
                Stretch = Stretch.Uniform,
                HorizontalAlignment = HorizontalAlignment.Center,
            };
            panel.Children.Add(path);
            panel.Children.Add(new TextBlock
            {
                Text = name,
                FontSize = 10,
                HorizontalAlignment = HorizontalAlignment.Center,
                Foreground = (Brush)FindResource("GlassTextSecondary"),
            });
            IconPanel.Children.Add(panel);
        }
    }

    private void ApplyAppearance()
    {
        var prefs = App.State.Config.Preferences;
        prefs.Theme = _themePreference;
        prefs.AdaptiveTint = AdaptiveTintCheck.IsChecked == true;
        prefs.ReduceMotion = ReduceMotionCheck.IsChecked == true;
        prefs.GlassIntensity = IntensitySlider.Value;
        GlassTheme.Apply(prefs.Theme, prefs.AccentHex, prefs.AdaptiveTint, prefs.GlassIntensity);
        SpringAnimator.ReduceMotion = prefs.ReduceMotion;
        IntensityText.Text = prefs.GlassIntensity.ToString("0.0");
    }

    private void ThemeAuto_Click(object sender, RoutedEventArgs e) { _themePreference = "Auto"; ApplyAppearance(); }

    private void ThemeLight_Click(object sender, RoutedEventArgs e) { _themePreference = "Light"; ApplyAppearance(); }

    private void ThemeDark_Click(object sender, RoutedEventArgs e) { _themePreference = "Dark"; ApplyAppearance(); }

    private void Appearance_Click(object sender, RoutedEventArgs e) => ApplyAppearance();

    private void Intensity_Changed(object sender, RoutedPropertyChangedEventArgs<double> e)
    {
        if (!IsLoaded) return;
        ApplyAppearance();
    }

    private void Accent_Click(object sender, RoutedEventArgs e)
    {
        if (sender is Button { Tag: string hex })
        {
            App.State.Config.Preferences.AccentHex = hex;
            ApplyAppearance();
        }
    }

    private void SpringButton_Click(object sender, RoutedEventArgs e)
    {
        // Spring demo: press down fast, then spring back with overshoot.
        SpringAnimator.AnimateScale(SpringCard, 0.94, SpringAnimator.SpringPreset.Snappy);
        var timer = new System.Windows.Threading.DispatcherTimer { Interval = TimeSpan.FromMilliseconds(120) };
        timer.Tick += (_, _) =>
        {
            timer.Stop();
            SpringAnimator.AnimateScale(SpringCard, 1.0, SpringAnimator.SpringPreset.Bouncy);
        };
        timer.Start();
    }
}
