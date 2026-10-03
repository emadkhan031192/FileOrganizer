using System.Windows;
using System.Windows.Controls;
using Microsoft.Win32;

namespace FileOrganizer.App;

/// <summary>Small shared dialogs (folder/file pickers, a one-line text prompt).</summary>
public static class Dialogs
{
    public static string? PickFolder(string title = "Select folder")
    {
        var dialog = new OpenFolderDialog { Title = title };
        return dialog.ShowDialog() == true ? dialog.FolderName : null;
    }

    public static List<string> PickFiles(string title = "Select files")
    {
        var dialog = new OpenFileDialog { Title = title, Multiselect = true, CheckFileExists = true };
        return dialog.ShowDialog() == true ? dialog.FileNames.ToList() : new List<string>();
    }

    /// <summary>The files to act on: Explorer's current selection when there is one, otherwise a file picker.</summary>
    public static List<string> GetFilesToActOn()
    {
        var selected = ExplorerSelection.GetSelectedFiles();
        return selected.Count > 0 ? selected : PickFiles("Select the files to organize");
    }

    public static string? Prompt(string title, string label, string initial = "")
    {
        var window = new Window
        {
            Title = title,
            Width = 360,
            Height = 150,
            WindowStartupLocation = WindowStartupLocation.CenterScreen,
            ResizeMode = ResizeMode.NoResize,
        };
        var panel = new StackPanel { Margin = new Thickness(12) };
        panel.Children.Add(new TextBlock { Text = label, Margin = new Thickness(0, 0, 0, 6) });
        var box = new TextBox { Text = initial };
        panel.Children.Add(box);
        var buttons = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right, Margin = new Thickness(0, 10, 0, 0) };
        var ok = new Button { Content = "OK", IsDefault = true, Width = 70 };
        var cancel = new Button { Content = "Cancel", IsCancel = true, Width = 70 };
        ok.Click += (_, _) => { window.DialogResult = true; window.Close(); };
        buttons.Children.Add(ok);
        buttons.Children.Add(cancel);
        panel.Children.Add(buttons);
        window.Content = panel;
        box.Focus();
        box.SelectAll();
        return window.ShowDialog() == true && !string.IsNullOrWhiteSpace(box.Text) ? box.Text.Trim() : null;
    }

    public static void Info(string message, string title = "File Organizer") =>
        MessageBox.Show(message, title, MessageBoxButton.OK, MessageBoxImage.Information);

    public static void Error(string message, string title = "File Organizer") =>
        MessageBox.Show(message, title, MessageBoxButton.OK, MessageBoxImage.Error);

    public static bool Confirm(string message, string title = "File Organizer") =>
        MessageBox.Show(message, title, MessageBoxButton.YesNo, MessageBoxImage.Question) == MessageBoxResult.Yes;
}
