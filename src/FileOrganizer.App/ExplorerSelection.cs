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

    /// <summary>The folder shown in the foreground/most recent Explorer window (for the Quick Bar ⚡ Organize button), or null.</summary>
    public static string? GetExplorerFolderPath()
    {
        object? shell = null;
        try
        {
            var shellType = Type.GetTypeFromProgID("Shell.Application");
            if (shellType is null) return null;
            shell = Activator.CreateInstance(shellType);
            if (shell is null) return null;

            dynamic windows = ((dynamic)shell).Windows();
            for (var i = (int)windows.Count - 1; i >= 0; i--) // most recently opened window first
            {
                try
                {
                    dynamic window = windows.Item(i);
                    string path = window.Document.Folder.Self.Path;
                    if (!string.IsNullOrWhiteSpace(path) && Directory.Exists(path))
                        return path;
                }
                catch (COMException) { /* not a folder view */ }
                catch (Exception) { /* keep looking */ }
            }
        }
        catch (Exception) { /* Shell automation unavailable */ }
        finally
        {
            if (shell is not null && Marshal.IsComObject(shell))
                Marshal.ReleaseComObject(shell);
        }
        return null;
    }
}
