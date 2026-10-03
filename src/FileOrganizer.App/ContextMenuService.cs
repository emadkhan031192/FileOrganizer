using System.IO;
using Microsoft.Win32;
using FileOrganizer.Core;

namespace FileOrganizer.App;

/// <summary>
/// Explorer integration (spec §2) using only supported Windows mechanisms — no Explorer hacks:
///  1. Classic context menu (HKCU\Software\Classes, appears under "Show more options" on Windows 11),
///     registered for BOTH files (*) and folders (Directory):
///       Right-click a file or folder → "Move to Organizer" → [each Quick Destination] / "+ Add Destination".
///     The verbs invoke: FileOrganizer.exe --move-to &lt;destinationId&gt; "%1"
///  2. Send To shortcuts (one .lnk per destination) as an always-supported fallback.
///  3. Right-click a folder → "Add as Organizer Destination".
/// Re-run Install after destinations change (the Settings screen has a button; the installer does it once).
/// scripts\Install-ContextMenu.ps1 performs the same registration for installer/admin scenarios.
/// </summary>
public static class ContextMenuService
{
    private const string FileMenuRoot = @"Software\Classes\*\shell\MoveToOrganizer";
    private const string FolderMenuRoot = @"Software\Classes\Directory\shell\MoveToOrganizer";
    private const string FolderAddKey = @"Software\Classes\Directory\shell\OrganizerAddDestination";

    private static string ExePath => Environment.ProcessPath ?? Path.Combine(AppContext.BaseDirectory, "FileOrganizer.exe");

    public static void Install(AppConfig config)
    {
        InstallMenuAt(FileMenuRoot, config);
        InstallMenuAt(FolderMenuRoot, config);

        using var folderAdd = Registry.CurrentUser.CreateSubKey(FolderAddKey);
        folderAdd.SetValue("MUIVerb", "Add as Organizer Destination");
        using (var cmd = folderAdd.CreateSubKey("command"))
            cmd.SetValue("", $"\"{ExePath}\" --add-destination \"%1\"");

        InstallSendToShortcuts(config);
    }

    private static void InstallMenuAt(string menuRoot, AppConfig config)
    {
        using var menu = Registry.CurrentUser.CreateSubKey(menuRoot);
        menu.SetValue("MUIVerb", "Move to Organizer");
        menu.SetValue("SubCommands", "");
        menu.SetValue("Icon", $"\"{ExePath}\",0");

        // Clear old verbs so renamed/removed destinations disappear.
        try { Registry.CurrentUser.DeleteSubKeyTree(menuRoot + @"\shell", throwOnMissingSubKey: false); } catch (ArgumentException) { }

        foreach (var dest in config.Destinations.OrderBy(d => d.SortOrder))
        {
            using var verb = Registry.CurrentUser.CreateSubKey($@"{menuRoot}\shell\MoveTo_{dest.Id}");
            verb.SetValue("MUIVerb", $"{dest.Icon} {dest.Name}");
            using var cmd = verb.CreateSubKey("command");
            cmd.SetValue("", $"\"{ExePath}\" --move-to \"{dest.Id}\" \"%1\"");
        }

        using var add = Registry.CurrentUser.CreateSubKey($@"{menuRoot}\shell\ZZ_AddDestination");
        add.SetValue("MUIVerb", "+ Add Destination…");
        using (var cmd = add.CreateSubKey("command"))
            cmd.SetValue("", $"\"{ExePath}\" --add-destination \"%1\"");
    }

    public static void Uninstall(AppConfig config)
    {
        try { Registry.CurrentUser.DeleteSubKeyTree(FileMenuRoot, throwOnMissingSubKey: false); } catch (ArgumentException) { }
        try { Registry.CurrentUser.DeleteSubKeyTree(FolderMenuRoot, throwOnMissingSubKey: false); } catch (ArgumentException) { }
        try { Registry.CurrentUser.DeleteSubKeyTree(FolderAddKey, throwOnMissingSubKey: false); } catch (ArgumentException) { }
        foreach (var file in Directory.EnumerateFiles(SendToFolder, "Move to *.lnk"))
            File.Delete(file);
    }

    private static string SendToFolder =>
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
                     @"Microsoft\Windows\SendTo");

    /// <summary>Send To fallback: one shortcut per destination (WScript.Shell COM — part of Windows, no extra dependency).</summary>
    private static void InstallSendToShortcuts(AppConfig config)
    {
        try
        {
            Directory.CreateDirectory(SendToFolder);
            foreach (var file in Directory.EnumerateFiles(SendToFolder, "Move to *.lnk"))
                File.Delete(file);

            var shellType = Type.GetTypeFromProgID("WScript.Shell");
            if (shellType is null) return;
            dynamic shell = Activator.CreateInstance(shellType)!;
            foreach (var dest in config.Destinations.OrderBy(d => d.SortOrder))
            {
                var linkPath = Path.Combine(SendToFolder, $"Move to {dest.Name}.lnk");
                dynamic link = shell.CreateShortcut(linkPath);
                link.TargetPath = ExePath;
                link.Arguments = $"--move-to \"{dest.Id}\"";
                link.Description = $"Move file to {dest.Name} with File Organizer";
                link.Save();
            }
        }
        catch (Exception)
        {
            // Send To is a fallback; the context menu above is the primary path. Never fail Install over it.
        }
    }

    /// <summary>HKCU Run entry so File Organizer can start the Quick Bar with Windows (spec §16 preference).</summary>
    public static void SetRunAtStartup(bool enabled)
    {
        using var run = Registry.CurrentUser.CreateSubKey(@"Software\Microsoft\Windows\CurrentVersion\Run");
        if (enabled)
            run.SetValue("FileOrganizer", $"\"{ExePath}\"");
        else
            run.DeleteValue("FileOrganizer", throwOnMissingValue: false);
    }
}
