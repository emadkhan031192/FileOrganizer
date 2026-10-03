using System.IO;
using System.Windows;
using System.Windows.Controls;
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

        ShowOrganizeOnBarCheck.IsChecked = p.ShowOrganizeOnQuickBar;
        ShowRenameOnBarCheck.IsChecked = p.ShowRenameOnQuickBar;
        BarShowLabelsCheck.IsChecked = p.QuickBarShowLabels;
        SelectTag(BarSizeCombo, p.QuickBarSize);
        SelectTag(ThemeCombo, p.Theme);
        SelectTag(DockCombo, p.QuickBarDockMode);
        EnableHotkeysCheck.IsChecked = p.EnableHotkeys;
        SelectTag(ProfileCombo, p.OrganizeProfile);
        AccentBox.Text = p.AccentHex;

        ConfigPathText.Text = State.ConfigService.ConfigPath;
        RefreshDestinations();
    }

    private static void SelectTag(ComboBox combo, string tag)
    {
        foreach (ComboBoxItem item in combo.Items)
            if (item.Tag as string == tag) { combo.SelectedItem = item; return; }
    }

    private static string TagOf(ComboBox combo, string fallback) =>
        (combo.SelectedItem as ComboBoxItem)?.Tag as string ?? fallback;

    // ----- Destination management -----

    private void RefreshDestinations() =>
        DestinationsList.ItemsSource = State.Config.Destinations.OrderBy(d => d.SortOrder)
            .Select(d => $"{d.Icon} {d.Name}   —   {d.ExpandedPath}")
            .ToList();

    private Destination? SelectedDestination =>
        DestinationsList.SelectedIndex >= 0
            ? State.Config.Destinations.OrderBy(d => d.SortOrder).ElementAt(DestinationsList.SelectedIndex)
            : null;

    private void AddDestination_Click(object sender, RoutedEventArgs e)
    {
        var folder = Dialogs.PickFolder("Select a folder to add as a destination");
        if (folder is null) return;
        var name = Dialogs.Prompt("New destination", "Button label:", Path.GetFileName(folder.TrimEnd('\\', '/')));
        if (name is null) return;
        State.Config.Destinations.Add(new Destination
        {
            Name = name, Path = folder, Icon = "📁", SortOrder = State.Config.Destinations.Count,
        });
        State.Save();
        RefreshDestinations();
        RefreshBars();
    }

    private void RemoveDestination_Click(object sender, RoutedEventArgs e)
    {
        var dest = SelectedDestination;
        if (dest is null) { Dialogs.Info("Select a destination in the list first."); return; }
        if (!Dialogs.Confirm($"Remove destination \"{dest.Name}\"?\n(Files already in the folder are untouched.)")) return;
        foreach (var child in State.Config.Destinations.Where(d => d.ParentId == dest.Id))
            child.ParentId = null;
        State.Config.Destinations.Remove(dest);
        State.Save();
        RefreshDestinations();
        RefreshBars();
    }

    private void RenameDestination_Click(object sender, RoutedEventArgs e)
    {
        var dest = SelectedDestination;
        if (dest is null) { Dialogs.Info("Select a destination in the list first."); return; }
        var name = Dialogs.Prompt("Rename destination", "New name:", dest.Name);
        if (name is null) return;
        dest.Name = name;
        State.Save();
        RefreshDestinations();
        RefreshBars();
    }

    private void OpenDestination_Click(object sender, RoutedEventArgs e)
    {
        var dest = SelectedDestination;
        if (dest is not null) MainWindow.OpenFolder(dest.ExpandedPath);
    }

    private void RefreshBars()
    {
        QuickBarWindow.RefreshIfExists();
        if (Owner is MainWindow main) main.RefreshDestinations();
        if (Application.Current.MainWindow is MainWindow appMain && !ReferenceEquals(Owner, appMain))
            appMain.RefreshDestinations();
    }

    // ----- Save -----

    private void Save_Click(object sender, RoutedEventArgs e)
    {
        var p = State.Config.Preferences;
        p.ConfirmBeforeOrganize = ConfirmBeforeOrganizeCheck.IsChecked == true;
        p.NotifyAfterQuickMove = NotifyAfterQuickMoveCheck.IsChecked == true;
        p.ShowQuickBarOnStart = ShowQuickBarCheck.IsChecked == true;
        p.IncludeSubfolders = IncludeSubfoldersCheck.IsChecked == true;
        p.RunAtStartup = RunAtStartupCheck.IsChecked == true;

        p.ShowOrganizeOnQuickBar = ShowOrganizeOnBarCheck.IsChecked == true;
        p.ShowRenameOnQuickBar = ShowRenameOnBarCheck.IsChecked == true;
        p.QuickBarShowLabels = BarShowLabelsCheck.IsChecked == true;
        p.QuickBarSize = TagOf(BarSizeCombo, "M");
        p.Theme = TagOf(ThemeCombo, "Light");
        p.QuickBarDockMode = TagOf(DockCombo, "Free");
        p.EnableHotkeys = EnableHotkeysCheck.IsChecked == true;
        p.OrganizeProfile = TagOf(ProfileCombo, "SortedDocuments");
        p.AccentHex = string.IsNullOrWhiteSpace(AccentBox.Text) ? "#2563EB" : AccentBox.Text.Trim();

        State.Save();
        ContextMenuService.SetRunAtStartup(p.RunAtStartup);
        App.ApplyTheme(p);
        RefreshBars();
        if (Application.Current.MainWindow is MainWindow appMain)
            appMain.ReregisterHotkeys();
        DialogResult = true;
        Close();
    }

    private void InstallMenu_Click(object sender, RoutedEventArgs e)
    {
        ContextMenuService.Install(State.Config);
        Dialogs.Info("Explorer context menu installed for files and folders.\nRight-click any file or folder → Show more options → Move to Organizer.");
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
            App.ApplyTheme(State.Config.Preferences);
            RefreshDestinations();
            RefreshBars();
            Dialogs.Info("Settings imported.");
            Close();
        }
        catch (Exception ex)
        {
            Dialogs.Error($"Could not import settings:\n{ex.Message}");
        }
    }
}
