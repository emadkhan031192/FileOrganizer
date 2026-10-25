using System.Windows;

namespace FileOrganizer.App;

/// <summary>Overall change: a 60-second first run instead of a settings maze. Shown once.</summary>
public partial class FirstRunWindow : Window
{
    public FirstRunWindow()
    {
        InitializeComponent();
        DestinationsText.Text = string.Join("  ·  ", App.State.Config.Destinations.Select(d => $"{d.Icon} {d.Name}"));
        WatchCheck.IsChecked = App.State.Config.Preferences.WatchDownloads;
        WheelOnlyCheck.IsChecked = App.State.Config.Preferences.WheelOnlyMode;
    }

    private void Done_Click(object sender, RoutedEventArgs e)
    {
        var p = App.State.Config.Preferences;
        p.WatchDownloads = WatchCheck.IsChecked == true;
        p.WheelOnlyMode = WheelOnlyCheck.IsChecked == true;
        p.SetupDone = true;
        App.State.Save();
        DialogResult = true;
        Close();
    }
}
