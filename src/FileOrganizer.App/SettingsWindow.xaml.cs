using System.Windows;
using Microsoft.Win32;
using FileOrganizer.Core;

namespace FileOrganizer.App;

public partial class SettingsWindow : Window
{
    private AppState State => App.State;

    public SettingsWindow()
    {
        InitializeComponent();
        var p = State.Config.Preferences;
        ConfirmBeforeOrganizeCheck.IsChecked = p.ConfirmBeforeOrganize;
        NotifyAfterQuickMoveCheck.IsChecked = p.NotifyAfterQuickMove;
        ShowQuickBarCheck.IsChecked = p.ShowQuickBarOnStart;
        IncludeSubfoldersCheck.IsChecked = p.IncludeSubfolders;
        RunAtStartupCheck.IsChecked = p.RunAtStartup;
        ConfigPathText.Text = State.ConfigService.ConfigPath;
    }

    private void Save_Click(object sender, RoutedEventArgs e)
    {
        var p = State.Config.Preferences;
        p.ConfirmBeforeOrganize = ConfirmBeforeOrganizeCheck.IsChecked == true;
        p.NotifyAfterQuickMove = NotifyAfterQuickMoveCheck.IsChecked == true;
        p.ShowQuickBarOnStart = ShowQuickBarCheck.IsChecked == true;
        p.IncludeSubfolders = IncludeSubfoldersCheck.IsChecked == true;
        p.RunAtStartup = RunAtStartupCheck.IsChecked == true;
        State.Save();
        ContextMenuService.SetRunAtStartup(p.RunAtStartup);
        DialogResult = true;
        Close();
    }

    private void InstallMenu_Click(object sender, RoutedEventArgs e)
    {
        ContextMenuService.Install(State.Config);
        Dialogs.Info("Explorer context menu installed.\nRight-click any file → Show more options → Move to Organizer.");
    }

    private void UninstallMenu_Click(object sender, RoutedEventArgs e)
    {
        ContextMenuService.Uninstall(State.Config);
        Dialogs.Info("Explorer context menu removed.");
    }

    private void Export_Click(object sender, RoutedEventArgs e)
    {
        var dialog = new SaveFileDialog { Filter = "JSON settings (*.json)|*.json", FileName = "file-organizer-settings.json" };
        if (dialog.ShowDialog() == true)
        {
            State.ConfigService.Export(State.Config, dialog.FileName);
            Dialogs.Info($"Settings exported to:\n{dialog.FileName}");
        }
    }

    private void Import_Click(object sender, RoutedEventArgs e)
    {
        var dialog = new OpenFileDialog { Filter = "JSON settings (*.json)|*.json" };
        if (dialog.ShowDialog() != true) return;
        try
        {
            var imported = State.ConfigService.Import(dialog.FileName);
            if (!Dialogs.Confirm("Replace ALL current destinations, rules and settings with the imported file?")) return;
            // Copy into the live config object so every open window sees the same instance.
            State.Config.Destinations = imported.Destinations;
            State.Config.ExtensionMap = imported.ExtensionMap;
            State.Config.Rules = imported.Rules;
            State.Config.RenamePresets = imported.RenamePresets;
            State.Config.RecentFolders = imported.RecentFolders;
            State.Config.Preferences = imported.Preferences;
            State.Save();
            Dialogs.Info("Settings imported. Reopen windows to see the new destinations.");
            Close();
        }
        catch (Exception ex)
        {
            Dialogs.Error($"Could not import settings:\n{ex.Message}");
        }
    }
}
