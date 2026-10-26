using System.IO;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Shell;

namespace FileOrganizer.App.Glass;

/// <summary>
/// Phase 1 backdrop + theme helpers.
/// Honest implementation notes, stated in code so later phases keep them:
/// - Hub and large windows get a REAL backdrop on Windows 11 22H2+ via DWM
///   (DWMWA_SYSTEMBACKDROP_TYPE, attribute 38: Mica = 2, Acrylic = 3) with
///   WindowChrome and without AllowsTransparency.
/// - On Windows 10 (or if DWM fails) the caller falls back to the fake-glass
///   surface brushes in GlassTokens.xaml — no blur, but the same tint/edge.
/// - Orb, wheel and petals always use fake glass (gradients, edge stroke,
///   shadow), because real blur does not work on small transparent layered
///   windows. That is a platform limit, not a styling choice.
/// - No network, no telemetry, no keystroke or content inspection anywhere.
/// </summary>
public static class BackdropHelper
{
    public enum BackdropKind
    {
        Auto,
        Mica,
        Acrylic,
        None,
    }

    private const int DwmwaSystemBackdropType = 38;
    private const int DwmwaUseImmersiveDarkMode = 20;
    private const int DwmsbtAuto = 0;
    private const int DwmsbtNone = 1;
    private const int DwmsbtMainWindow = 2; // Mica
    private const int DwmsbtTransientWindow = 3; // Acrylic

    [DllImport("dwmapi.dll", PreserveSig = true)]
    private static extern int DwmSetWindowAttribute(IntPtr hwnd, int attr, ref int value, int size);

    public static bool IsWindows11_22H2Plus()
    {
        if (!OperatingSystem.IsWindows()) return false;
        try
        {
            var version = Environment.OSVersion.Version;
            return version.Major >= 10 && version.Build >= 22621;
        }
        catch
        {
            return false;
        }
    }

    /// <summary>
    /// Apply a real DWM backdrop to a normal (non-transparent) window.
    /// Returns true only when DWM accepted the backdrop; the caller must
    /// fall back to fake-glass brushes when this returns false.
    /// </summary>
    public static bool TryApplyBackdrop(Window window, BackdropKind kind, bool darkMode)
    {
        if (!IsWindows11_22H2Plus() || kind == BackdropKind.None) return false;
        try
        {
            var chrome = new WindowChrome
            {
                CaptionHeight = 32,
                ResizeBorderThickness = new Thickness(6),
                GlassFrameThickness = new Thickness(0),
                CornerRadius = new CornerRadius(20),
                UseAeroCaptionButtons = true,
            };
            WindowChrome.SetWindowChrome(window, chrome);
            window.Background = Brushes.Transparent;

            var hwnd = new WindowInteropHelper(window).Handle;
            if (hwnd == IntPtr.Zero) return false;
            var backdrop = kind switch
            {
                BackdropKind.Mica => DwmsbtMainWindow,
                BackdropKind.Acrylic => DwmsbtTransientWindow,
                _ => DwmsbtAuto,
            };
            var result = DwmSetWindowAttribute(hwnd, DwmwaSystemBackdropType, ref backdrop, sizeof(int));
            if (result != 0) return false;
            var dark = darkMode ? 1 : 0;
            DwmSetWindowAttribute(hwnd, DwmwaUseImmersiveDarkMode, ref dark, sizeof(int));
            return true;
        }
        catch
        {
            return false;
        }
    }

    /// <summary>Windows "Apps use light/dark" setting; false (light) on any failure.</summary>
    public static bool IsSystemDark()
    {
        try
        {
            var value = Microsoft.Win32.Registry.GetValue(
                @"HKEY_CURRENT_USER\Software\Microsoft\Windows\CurrentVersion\Themes\Personalize",
                "AppsUseLightTheme", 1);
            return value is int intValue && intValue == 0;
        }
        catch
        {
            return false;
        }
    }

    /// <summary>Resolve a Theme preference ("Light" / "Dark" / "Auto") to "Light" or "Dark".</summary>
    public static string ResolveTheme(string? preference)
    {
        if (string.Equals(preference, "Dark", StringComparison.OrdinalIgnoreCase)) return "Dark";
        if (string.Equals(preference, "Light", StringComparison.OrdinalIgnoreCase)) return "Light";
        return IsSystemDark() ? "Dark" : "Light";
    }
}

/// <summary>
/// Applies the resolved theme, accent, adaptive tint and glass intensity to
/// the application's DynamicResource brushes, so every glass surface that
/// uses DynamicResource updates at once. Safe to call repeatedly.
/// </summary>
public static class GlassTheme
{
    public static string CurrentTheme { get; private set; } = "Light";

    public static void Apply(string? themePreference, string? accentHex, bool adaptiveTint, double glassIntensity)
    {
        var resources = Application.Current?.Resources;
        if (resources is null) return;
        CurrentTheme = BackdropHelper.ResolveTheme(themePreference);
        var dark = CurrentTheme == "Dark";
        var intensity = Math.Clamp(glassIntensity, 0.5, 1.5);

        SetBrush(resources, "GlassTextPrimary", dark ? "GlassTextPrimaryDark" : "GlassTextPrimaryLight");
        SetBrush(resources, "GlassTextSecondary", dark ? "GlassTextSecondaryDark" : "GlassTextSecondaryLight");
        SetBrush(resources, "GlassTextTertiary", dark ? "GlassTextTertiaryDark" : "GlassTextTertiaryLight");
        SetBrush(resources, "GlassSurface", dark ? "GlassSurfaceDark" : "GlassSurfaceLight");
        SetBrush(resources, "GlassEdge", dark ? "GlassEdgeDark" : "GlassEdgeLight");

        // Tint: wallpaper dominant colour when adaptive tint is on, otherwise
        // white (light) / black (dark). Intensity scales the tint opacity.
        var tintColor = Colors.White;
        if (dark) tintColor = Colors.Black;
        if (adaptiveTint && WallpaperTint.TryGetDominantColor(out var dominant))
            tintColor = dominant;
        var baseOpacity = dark ? 0.28 : 0.12;
        // CRASH FIX (reported 2026-10-26: "Cannot set a property on object
        // '#FFFFFFFF' because it is in a read-only state" at startup, process
        // left Suspended under Windows Problem Reporting with no window).
        // Brushes loaded from a ResourceDictionary are frozen (read-only) on
        // a real Windows machine — mutating GlassTint (white, #FFFFFFFF) or
        // the accent brushes in place throws InvalidOperationException.
        // Never mutate a resource brush: replace the dictionary entry with
        // a fresh, unfrozen brush instead.
        resources["GlassTint"] = new SolidColorBrush(tintColor)
        {
            Opacity = Math.Clamp(baseOpacity * intensity, 0.05, 0.45),
        };

        // Accent: user-picked hex, falling back to the default blue.
        if (TryParseHex(accentHex, out var accent))
        {
            resources["GlassAccent"] = new SolidColorBrush(accent);
            resources["GlassAccentSoft"] = new SolidColorBrush(accent) { Opacity = 0.15 };
            resources["GlassAccentHover"] = new SolidColorBrush(Darken(accent, 0.85));
        }
    }

    private static void SetBrush(ResourceDictionary resources, string target, string source)
    {
        // Clone, don't share: the source brush in the dictionary may be
        // frozen, and sharing it would make any later mutation throw the
        // same read-only error. Clone() returns a modifiable copy.
        if (resources[source] is Brush brush)
            resources[target] = brush.Clone();
    }

    private static bool TryParseHex(string? hex, out Color color)
    {
        color = Colors.Transparent;
        if (string.IsNullOrWhiteSpace(hex)) return false;
        try
        {
            var parsed = (Color)ColorConverter.ConvertFromString(hex);
            color = parsed;
            return true;
        }
        catch
        {
            return false;
        }
    }

    private static Color Darken(Color color, double factor) =>
        Color.FromRgb((byte)(color.R * factor), (byte)(color.G * factor), (byte)(color.B * factor));
}

/// <summary>
/// Adaptive tint: samples the current desktop wallpaper's dominant colour.
/// Only reads the wallpaper file path from the registry and decodes that
/// image at 32 px wide to compute an average colour. Any failure returns
/// false and the caller keeps the default tint. No image is stored or sent.
/// </summary>
public static class WallpaperTint
{
    public static bool TryGetDominantColor(out Color color)
    {
        color = Colors.Transparent;
        try
        {
            var path = Microsoft.Win32.Registry.GetValue(
                @"HKEY_CURRENT_USER\Control Panel\Desktop", "WallPaper", null) as string;
            if (string.IsNullOrWhiteSpace(path) || !File.Exists(path)) return false;

            var bitmap = new System.Windows.Media.Imaging.BitmapImage();
            bitmap.BeginInit();
            bitmap.UriSource = new Uri(path);
            bitmap.DecodePixelWidth = 32;
            bitmap.CacheOption = System.Windows.Media.Imaging.BitmapCacheOption.OnLoad;
            bitmap.EndInit();
            bitmap.Freeze();

            var formatted = new System.Windows.Media.Imaging.FormatConvertedBitmap(bitmap, System.Windows.Media.PixelFormats.Bgra32, null, 0);
            var width = formatted.PixelWidth;
            var height = formatted.PixelHeight;
            if (width == 0 || height == 0) return false;
            var pixels = new byte[width * height * 4];
            formatted.CopyPixels(pixels, width * 4, 0);
            long r = 0, g = 0, b = 0, count = 0;
            for (var i = 0; i < pixels.Length; i += 4)
            {
                var blue = pixels[i];
                var green = pixels[i + 1];
                var red = pixels[i + 2];
                // Skip near-black and near-white pixels so a mostly dark or
                // light wallpaper does not collapse the tint to an extreme.
                var brightness = (red + green + blue) / 3;
                if (brightness < 24 || brightness > 232) continue;
                r += red; g += green; b += blue; count++;
            }
            if (count == 0) return false;
            color = Color.FromRgb((byte)(r / count), (byte)(g / count), (byte)(b / count));
            return true;
        }
        catch
        {
            return false;
        }
    }
}
