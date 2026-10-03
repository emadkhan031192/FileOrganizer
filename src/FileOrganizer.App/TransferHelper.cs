using System.IO;
using System.Windows;
using FileOrganizer.Core;

namespace FileOrganizer.App;

/// <summary>
/// Pre-move counts (recommendation 4): totals for a transfer, and the confirmation
/// shown before big moves (any folder involved, or 10+ top-level items).
/// Small quick moves stay one-click with no prompt — that's the product's core promise.
/// </summary>
public static class TransferHelper
{
    public sealed record Totals(int TopLevelItems, int FileCount, long Bytes, bool HasFolders)
    {
        public string Describe() =>
            $"{TopLevelItems} item(s) · {FileCount} file(s) · {PathHelpers.FormatSize(Bytes)}";
    }

    public static Totals Compute(IEnumerable<string> items)
    {
        var top = 0;
        var files = 0;
        long bytes = 0;
        var hasFolders = false;

        foreach (var item in items)
        {
            top++;
            if (Directory.Exists(item))
            {
                hasFolders = true;
                try
                {
                    foreach (var path in Directory.EnumerateFiles(item, "*", SearchOption.AllDirectories))
                    {
                        files++;
                        try { bytes += new FileInfo(path).Length; } catch (IOException) { }
                    }
                }
                catch (IOException) { }
                catch (UnauthorizedAccessException) { }
            }
            else if (File.Exists(item))
            {
                files++;
                try { bytes += new FileInfo(item).Length; } catch (IOException) { }
            }
        }
        return new Totals(top, files, bytes, hasFolders);
    }

    public static bool NeedsConfirmation(Totals totals) => totals.HasFolders || totals.TopLevelItems >= 10;

    /// <summary>Returns false when the user declines the pre-move confirmation.</summary>
    public static async Task<bool> ConfirmIfNeededAsync(Window owner, IReadOnlyCollection<string> items, string destinationName, TransferMode mode)
    {
        var totals = await Task.Run(() => Compute(items));
        if (!NeedsConfirmation(totals))
            return true;
        var verb = mode == TransferMode.Copy ? "Copy" : "Move";
        return DialogsConfirm(owner,
            $"{verb} to \"{destinationName}\"?\n\n{totals.Describe()}\n\nName conflicts are handled per your If-exists setting and are never silently overwritten.");
    }

    private static bool DialogsConfirm(Window owner, string message) =>
        MessageBox.Show(owner, message, "File Organizer", MessageBoxButton.OKCancel, MessageBoxImage.Question) == MessageBoxResult.OK;

    /// <summary>Runs a transfer with the progress window (recommendation 4) and returns the result.</summary>
    public static async Task<BatchResult> RunTransferWithProgressAsync(
        Window owner, IReadOnlyCollection<string> items, Destination destination, TransferMode mode, ConflictPolicy conflict)
    {
        var progressWindow = new ProgressWindow($"{mode} {items.Count} item(s) → {destination.Name}")
        {
            Owner = owner,
            WindowStartupLocation = WindowStartupLocation.CenterScreen,
        };
        var progress = progressWindow.CreateProgress();
        progressWindow.Show();
        try
        {
            return await Task.Run(() => App.State.Ops.TransferFiles(
                items, destination.ExpandedPath, mode, conflict, progress, progressWindow.Cts.Token));
        }
        finally
        {
            progressWindow.Close();
        }
    }

    /// <summary>Runs an organized-preview apply with the progress window.</summary>
    public static async Task<BatchResult> RunApplyWithProgressAsync(
        Window owner, IReadOnlyCollection<(string Source, string Destination)> pairs, TransferMode mode, ConflictPolicy conflict)
    {
        var progressWindow = new ProgressWindow($"{mode} {pairs.Count} file(s)")
        {
            Owner = owner,
            WindowStartupLocation = WindowStartupLocation.CenterScreen,
        };
        var progress = progressWindow.CreateProgress();
        progressWindow.Show();
        try
        {
            return await Task.Run(() => App.State.Ops.ApplyPairs(pairs, mode, conflict, progress, progressWindow.Cts.Token));
        }
        finally
        {
            progressWindow.Close();
        }
    }
}
