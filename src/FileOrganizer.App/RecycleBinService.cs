using System.IO;
using System.Linq;
using System.Runtime.InteropServices;

namespace FileOrganizer.App;

/// <summary>
/// Sends files/folders to the Windows Recycle Bin via SHFileOperation (FO_DELETE + FOF_ALLOWUNDO),
/// so they stay recoverable from the Recycle Bin. This is NOT a File Organizer Undo operation:
/// restoring is done from the Windows Recycle Bin itself, and the UI warns about that first.
/// </summary>
public static class RecycleBinService
{
    private const int FO_DELETE = 0x0003;
    private const ushort FOF_ALLOWUNDO = 0x0040;
    private const ushort FOF_NOCONFIRMATION = 0x0010;

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct SHFILEOPSTRUCT
    {
        public IntPtr hwnd;
        public int wFunc;
        public string pFrom;
        public string? pTo;
        public ushort fFlags;
        public bool fAnyOperationsAborted;
        public IntPtr hNameMappings;
        public string? lpszProgressTitle;
    }

    [DllImport("shell32.dll", CharSet = CharSet.Unicode)]
    private static extern int SHFileOperation(ref SHFILEOPSTRUCT lpFileOp);

    /// <summary>Returns (success, error). Paths that no longer exist are skipped, not counted as failures.</summary>
    public static (bool Success, string? Error) SendToRecycleBin(IEnumerable<string> paths)
    {
        var existing = paths.Where(p => File.Exists(p) || Directory.Exists(p)).ToList();
        if (existing.Count == 0)
            return (false, "Nothing to recycle — the file(s) no longer exist.");
        // SHFileOperation wants a double-null-terminated list.
        var from = string.Join('\0', existing) + "\0\0";
        var op = new SHFILEOPSTRUCT
        {
            wFunc = FO_DELETE,
            pFrom = from,
            fFlags = (ushort)(FOF_ALLOWUNDO | FOF_NOCONFIRMATION),
        };
        var code = SHFileOperation(ref op);
        if (code != 0)
            return (false, $"Windows could not recycle the file(s) (error {code}). Close the file if it is open and try again.");
        if (op.fAnyOperationsAborted)
            return (false, "Recycling was cancelled part-way through.");
        return (true, null);
    }

    /// <summary>First-time warning + recycle, run on a background thread. Returns a message for the toast.</summary>
    public static async Task<string> RecycleAsync(List<string> items)
    {
        var prefs = App.State.Config.Preferences;
        if (!prefs.RecycleBinWarned)
        {
            var ok = System.Windows.MessageBox.Show(
                "Files sent to the Recycle Bin can be restored from the Windows Recycle Bin.\n\nFile Organizer's own ↶ Undo will NOT restore them.\n\nContinue?",
                "Recycle Bin", System.Windows.MessageBoxButton.OKCancel, System.Windows.MessageBoxImage.Warning)
                == System.Windows.MessageBoxResult.OK;
            if (!ok)
                return "Recycle cancelled — nothing was deleted.";
            prefs.RecycleBinWarned = true;
            App.State.Save();
        }
        var (success, error) = await Task.Run(() => SendToRecycleBin(items));
        return success
            ? $"🗑 Sent {items.Count} item(s) to the Recycle Bin. Restore them from the Windows Recycle Bin if needed."
            : $"⚠ Recycle failed: {error}";
    }

    public static void OpenRecycleBin()
    {
        try
        {
            System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo("explorer.exe", "shell:RecycleBinFolder") { UseShellExecute = true });
        }
        catch { /* opening the bin is best-effort */ }
    }
}
