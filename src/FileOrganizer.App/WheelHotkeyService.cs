using System.Windows.Input;
using FileOrganizer.Core;

namespace FileOrganizer.App;

/// <summary>
/// Phase 2 wheel entry points, driven by the low-level keyboard hook.
/// Two flows, both scoped to Explorer / Desktop foreground windows:
/// - Keyboard flow: the configured global hotkey (default Ctrl+`) with files
///   selected in Explorer opens the Move wheel at the cursor.
/// - Drag flow: while the user drags files in Explorer (left button held),
///   tapping a wheel key (default Shift = Move, Ctrl = Tools, Z = Actions)
///   opens that wheel at the cursor. The user releases the key and keeps
///   dragging; the wheel stays open until a drop on a petal or Esc.
/// The service never logs keys: it only reacts to the configured hotkey and
/// wheel keys, and passes everything else through untouched.
/// </summary>
public static class WheelHotkeyService
{
    private static bool _subscribed;

    public static void Start()
    {
        if (_subscribed) return;
        _subscribed = true;
        KeyboardHook.KeyPressed += OnKeyPressed;
        KeyboardHook.Install();
    }

    public static void Stop()
    {
        if (!_subscribed) return;
        _subscribed = false;
        KeyboardHook.KeyPressed -= OnKeyPressed;
        KeyboardHook.Uninstall();
    }

    private static AppPreferences Prefs => App.State.Config.Preferences;

    private static void OnKeyPressed(object? sender, KeyboardHook.WheelKeyEventArgs e)
    {
        // A wheel is open: it owns navigation keys.
        if (WheelWindow.Current is WheelWindow wheel)
        {
            e.Handled = wheel.HandleHookKey(e.Key, e.KeyDown);
            return;
        }

        if (!e.KeyDown) return;
        if (!KeyboardHook.IsExplorerOrDesktopForeground()) return;

        // Keyboard flow: global hotkey.
        if (Prefs.OpenWheelFromKeyboard && IsHotkey(e.Key, Prefs.WheelHotkey))
        {
            var items = ExplorerSelection.GetSelectedFiles();
            WheelWindow.Show(WheelWindow.WheelKind.Move, items, fromDrag: false);
            e.Handled = true;
            return;
        }

        // Drag flow: a wheel key tapped while the left button is held (dragging).
        if (Prefs.OpenWheelOnDrag && KeyboardHook.IsLeftButtonDown())
        {
            var kind = MatchWheelKey(e.Key);
            if (kind is not null)
            {
                var items = ExplorerSelection.GetSelectedFiles();
                WheelWindow.Show(kind.Value, items, fromDrag: true);
                e.Handled = true;
            }
        }
    }

    /// <summary>Matches a key event against a "Ctrl+`" style hotkey string.</summary>
    internal static bool IsHotkey(Key key, string hotkey)
    {
        if (string.IsNullOrWhiteSpace(hotkey)) return false;
        var parts = hotkey.Split('+', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries);
        if (parts.Length == 0) return false;
        var main = parts[^1];
        var wantCtrl = parts.Contains("Ctrl", StringComparer.OrdinalIgnoreCase);
        var wantShift = parts.Contains("Shift", StringComparer.OrdinalIgnoreCase);
        var wantAlt = parts.Contains("Alt", StringComparer.OrdinalIgnoreCase);
        if (wantCtrl != KeyboardHook.IsKeyDown(0x11)) return false;
        if (wantShift != KeyboardHook.IsKeyDown(0x10)) return false;
        if (wantAlt != KeyboardHook.IsKeyDown(0x12)) return false;
        return key == ParseKey(main);
    }

    private static WheelWindow.WheelKind? MatchWheelKey(Key key)
    {
        var prefs = Prefs;
        if (IsWheelKey(key, prefs.WheelMoveKey)) return WheelWindow.WheelKind.Move;
        if (IsWheelKey(key, prefs.WheelToolsKey)) return WheelWindow.WheelKind.Tools;
        if (IsWheelKey(key, prefs.WheelActionsKey)) return WheelWindow.WheelKind.Actions;
        return null;
    }

    internal static bool IsWheelKey(Key key, string wheelKey)
    {
        if (string.Equals(wheelKey, "Shift", StringComparison.OrdinalIgnoreCase))
            return key is Key.LeftShift or Key.RightShift;
        if (string.Equals(wheelKey, "Ctrl", StringComparison.OrdinalIgnoreCase))
            return key is Key.LeftCtrl or Key.RightCtrl;
        if (string.Equals(wheelKey, "Alt", StringComparison.OrdinalIgnoreCase))
            return key is Key.LeftAlt or Key.RightAlt;
        return key == ParseKey(wheelKey);
    }

    internal static Key ParseKey(string token)
    {
        if (string.Equals(token, "`", StringComparison.Ordinal)) return Key.Oem3;
        if (token.Length == 1 && char.IsLetterOrDigit(token[0]))
            return (Key)Enum.Parse(typeof(Key), token.ToUpperInvariant());
        if (Enum.TryParse<Key>(token, ignoreCase: true, out var key))
            return key;
        return Key.None;
    }
}
