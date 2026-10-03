using System.IO;
using System.Runtime.InteropServices;

namespace FileOrganizer.App;

/// <summary>
/// Reads the files AND folders currently SELECTED in any open Windows Explorer window —
/// the heart of "Select a file → click a destination" (spec §1/§18).
/// Uses the official Shell automation interface (Shell.Application COM);
/// if no Explorer window has a selection, callers fall back to a file picker.
/// </summary>
public static class ExplorerSelection
{
    public static List<string> GetSelectedFiles()
    {
        var result = new List<string>();
        object? shell = null;
        try
        {
            var shellType = Type.GetTypeFromProgID("Shell.Application");
            if (shellType is null) return result;
            shell = Activator.CreateInstance(shellType);
            if (shell is null) return result;

            dynamic windows = ((dynamic)shell).Windows();
            for (var i = 0; i < (int)windows.Count; i++)
            {
                try
                {
                    dynamic window = windows.Item(i);
                    dynamic selected = window.Document.SelectedItems();
                    for (var j = 0; j < (int)selected.Count; j++)
                    {
                        string path = selected.Item(j).Path;
                        if ((File.Exists(path) || Directory.Exists(path)) && !result.Contains(path))
                            result.Add(path);
                    }
                }
                catch (COMException) { /* a shell window without a folder view (e.g. Control Panel) */ }
                catch (Exception) { /* keep scanning other Explorer windows */ }
            }
        }
        catch (Exception)
        {
            // Shell automation unavailable — caller falls back to the file picker.
        }
        finally
        {
            if (shell is not null && Marshal.IsComObject(shell))
                Marshal.ReleaseComObject(shell);
        }
        return result;
    }

    /// <summary>
    /// The folder of the Explorer window the user is actually looking at.
    /// Matches the foreground window handle first (Shell windows expose HWND),
    /// then falls back to the most recently opened Explorer window. Returns null when
    /// no Explorer folder window exists — callers then offer a folder picker instead of failing.
    /// </summary>
    public static string? GetExplorerFolderPath()
    {
        object? shell = null;
        try
        {
            var shellType = Type.GetTypeFromProgID("Shell.Application");
            if (shellType is null) return null;
            shell = Activator.CreateInstance(shellType);
            if (shell is null) return null;

            var foreground = GetForegroundWindow();
            string? fallback = null;

            dynamic windows = ((dynamic)shell).Windows();
            for (var i = (int)windows.Count - 1; i >= 0; i--) // most recently opened window first
            {
                try
                {
                    dynamic window = windows.Item(i);
                    string path = window.Document.Folder.Self.Path;
                    if (string.IsNullOrWhiteSpace(path) || !Directory.Exists(path))
                        continue;
                    fallback ??= path;
                    try
                    {
                        var hwnd = new IntPtr((int)window.HWND);
                        if (foreground != IntPtr.Zero && hwnd == foreground)
                            return path; // the Explorer window in front wins over any other
                    }
                    catch (Exception) { /* HWND not available on this window; fallback still applies */ }
                }
                catch (COMException) { /* not a folder view */ }
                catch (Exception) { /* keep looking */ }
            }
            return fallback;
        }
        catch (Exception) { /* Shell automation unavailable */ }
        finally
        {
            if (shell is not null && Marshal.IsComObject(shell))
                Marshal.ReleaseComObject(shell);
        }
        return null;
    }

    [System.Runtime.InteropServices.DllImport("user32.dll")]
    private static extern IntPtr GetForegroundWindow();
}
