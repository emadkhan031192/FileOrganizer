using System.Runtime.InteropServices;
using System.Text;
using System.Windows.Input;

namespace FileOrganizer.App;

/// <summary>
/// Phase 2 low-level keyboard hook (WH_KEYBOARD_LL).
///
/// Privacy and scope, by design — this is the whole point of the class, so it
/// is documented here and in the README:
/// - The hook NEVER logs, stores or transmits keystrokes. It receives only a
///   virtual-key code, and it immediately discards every key that is not one
///   of the wheel keys (arrows, Enter, Esc, Space, Backspace, a wheel-switch
///   key, a digit, or the configured global wheel hotkey).
/// - While a wheel is open, it swallows only the wheel-navigation keys, so
///   Explorer cannot act on them (e.g. an arrow key moving the Explorer
///   selection under the wheel). Every other key is passed through untouched.
/// - While no wheel is open, the hook passes everything through except the
///   configured global wheel hotkey — and only when the foreground window is
///   Explorer or the Desktop. Anywhere else, even the hotkey is passed through.
/// - The hook is installed when the main window opens and removed when it
///   closes. It is never installed by the gallery, a preview, or a dialog.
/// - It does not use RegisterHotKey for the wheel hotkey, precisely so the
///   Explorer/Desktop foreground check can be applied; RegisterHotKey would
///   fire everywhere.
/// </summary>
public static class KeyboardHook
{
    public sealed class WheelKeyEventArgs : EventArgs
    {
        public required Key Key { get; init; }
        public required bool KeyDown { get; init; }
        /// <summary>Set to true by a handler that consumed the key; the hook then swallows it.</summary>
        public bool Handled { get; set; }
    }

    public static event EventHandler<WheelKeyEventArgs>? KeyPressed;

    private const int WhKeyboardLl = 13;
    private const int WmKeydown = 0x0100;
    private const int WmKeyup = 0x0101;
    private const int WmSyskeydown = 0x0104;
    private const int WmSyskeyup = 0x0105;

    private delegate IntPtr HookProc(int nCode, IntPtr wParam, IntPtr lParam);

    private static HookProc? _proc;
    private static IntPtr _hookId = IntPtr.Zero;

    [StructLayout(LayoutKind.Sequential)]
    private struct Kbdllhookstruct
    {
        public int VkCode;
        public int ScanCode;
        public int Flags;
        public int Time;
        public IntPtr DwExtraInfo;
    }

    [DllImport("user32.dll", SetLastError = true)]
    private static extern IntPtr SetWindowsHookEx(int idHook, HookProc lpfn, IntPtr hMod, uint dwThreadId);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern bool UnhookWindowsHookEx(IntPtr hhk);

    [DllImport("user32.dll")]
    private static extern IntPtr CallNextHookEx(IntPtr hhk, int nCode, IntPtr wParam, IntPtr lParam);

    [DllImport("kernel32.dll", CharSet = CharSet.Unicode)]
    private static extern IntPtr GetModuleHandle(string? lpModuleName);

    [DllImport("user32.dll")]
    private static extern IntPtr GetForegroundWindow();

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    private static extern int GetClassName(IntPtr hWnd, StringBuilder lpClassName, int nMaxCount);

    [DllImport("user32.dll")]
    private static extern short GetAsyncKeyState(int vKey);

    public static bool IsInstalled => _hookId != IntPtr.Zero;

    public static void Install()
    {
        if (_hookId != IntPtr.Zero) return;
        _proc = HookCallback;
        _hookId = SetWindowsHookEx(WhKeyboardLl, _proc, GetModuleHandle(null), 0);
    }

    public static void Uninstall()
    {
        if (_hookId == IntPtr.Zero) return;
        UnhookWindowsHookEx(_hookId);
        _hookId = IntPtr.Zero;
        _proc = null;
    }

    /// <summary>True when the foreground window is an Explorer folder window or the Desktop.</summary>
    public static bool IsExplorerOrDesktopForeground()
    {
        try
        {
            var hwnd = GetForegroundWindow();
            if (hwnd == IntPtr.Zero) return false;
            var name = new StringBuilder(256);
            if (GetClassName(hwnd, name, name.Capacity) == 0) return false;
            var className = name.ToString();
            return className is "CabinetWClass" or "ExploreWClass" or "Progman" or "WorkerW" or "SHELLDLL_DefView";
        }
        catch
        {
            return false;
        }
    }

    /// <summary>True while the left mouse button is physically held down (used for the drag flow).</summary>
    public static bool IsLeftButtonDown()
    {
        try { return (GetAsyncKeyState(0x01) & 0x8000) != 0; }
        catch { return false; }
    }

    public static bool IsKeyDown(int virtualKey)
    {
        try { return (GetAsyncKeyState(virtualKey) & 0x8000) != 0; }
        catch { return false; }
    }

    private static IntPtr HookCallback(int nCode, IntPtr wParam, IntPtr lParam)
    {
        if (nCode < 0)
            return CallNextHookEx(_hookId, nCode, wParam, lParam);

        var message = wParam.ToInt32();
        var isDown = message is WmKeydown or WmSyskeydown;
        var isUp = message is WmKeyup or WmSyskeyup;
        if (!isDown && !isUp)
            return CallNextHookEx(_hookId, nCode, wParam, lParam);

        var data = Marshal.PtrToStructure<Kbdllhookstruct>(lParam);
        var key = KeyInterop.KeyFromVirtualKey(data.VkCode);
        if (key == Key.None)
            return CallNextHookEx(_hookId, nCode, wParam, lParam);

        var args = new WheelKeyEventArgs { Key = key, KeyDown = isDown };
        KeyPressed?.Invoke(null, args);
        if (args.Handled)
            return (IntPtr)1; // swallow: the wheel consumed this navigation key
        return CallNextHookEx(_hookId, nCode, wParam, lParam);
    }
}
