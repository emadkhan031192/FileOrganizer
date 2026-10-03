using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using FileOrganizer.Core;

namespace FileOrganizer.App;

public partial class QuickBarWindow : Window
{
    private static QuickBarWindow? _instance;

    private AppState State => App.State;

    private QuickBarWindow()
    {
        InitializeComponent();
        RefreshButtons();
        // Bottom-centre by default, just above the taskbar — close to Explorer without covering it.
        Loaded += (_, _) =>
        {
            Left = (SystemParameters.WorkArea.Width - ActualWidth) / 2;
            Top = SystemParameters.WorkArea.Bottom - ActualHeight - 12;
        };
    }

    /// <summary>Shows the single floating Quick Bar, creating it on first use.</summary>
    public static void ShowBar()
    {
        _instance ??= new QuickBarWindow();
        _instance.RefreshButtons();
        _instance.Show();
        _instance.Activate();
    }

    private void RefreshButtons()
    {
        ButtonsPanel.Children.Clear();
        foreach (var dest in State.Config.Destinations.Where(d => d.ParentId is null).OrderBy(d => d.SortOrder))
        {
            var button = new Button
            {
                Content = $"{dest.Icon} {dest.Name}",
                Tag = dest,
                AllowDrop = true,
                Margin = new Thickness(2),
                Padding = new Thickness(10, 5, 10, 5),
                ToolTip = $"Move selected file(s) to {dest.ExpandedPath}",
            };
            button.Click += Destination_Click;
            button.DragOver += (s, e) => { if (e.Data.GetDataPresent(DataFormats.FileDrop)) e.Effects = DragDropEffects.Move; };
            button.Drop += Destination_Drop;
            var menu = new ContextMenu();
            var open = new MenuItem { Header = "Open folder" };
            open.Click += (_, _) => MainWindow.OpenFolder(dest.ExpandedPath);
            menu.Items.Add(open);
            button.ContextMenu = menu;
            ButtonsPanel.Children.Add(button);
        }
    }

    private async void Destination_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not Button { Tag: Destination dest }) return;
        var files = ExplorerSelection.GetSelectedFiles();
        if (files.Count == 0) return; // nothing selected in Explorer — ignore instead of popping a picker over Explorer
        var result = await Task.Run(() => State.Ops.TransferFiles(files, dest.ExpandedPath,
            State.Config.Preferences.DefaultTransferMode, State.Config.Preferences.DefaultConflictPolicy));
        ToolTip = $"✓ {result.Succeeded} file(s) → {dest.Name}";
        if (Application.Current.MainWindow is MainWindow main) main.RefreshActivity();
    }

    private async void Destination_Drop(object sender, DragEventArgs e)
    {
        if (sender is not Button { Tag: Destination dest }) return;
        if (e.Data.GetData(DataFormats.FileDrop) is string[] dropped)
        {
            var files = dropped.Where(File.Exists).ToList();
            if (files.Count == 0) return;
            var result = await Task.Run(() => State.Ops.TransferFiles(files, dest.ExpandedPath,
                State.Config.Preferences.DefaultTransferMode, State.Config.Preferences.DefaultConflictPolicy));
            ToolTip = $"✓ {result.Succeeded} file(s) → {dest.Name}";
        }
    }

    private void Add_Click(object sender, RoutedEventArgs e)
    {
        var folder = Dialogs.PickFolder("Select a folder to add to the Quick Bar");
        if (folder is null) return;
        State.Config.Destinations.Add(new Destination
        {
            Name = Path.GetFileName(folder.TrimEnd('\\', '/')),
            Path = folder, Icon = "📁", SortOrder = State.Config.Destinations.Count,
        });
        State.Save();
        RefreshButtons();
        if (Application.Current.MainWindow is MainWindow main) main.RefreshDestinations();
    }

    private void Close_Click(object sender, RoutedEventArgs e) => Hide();

    private void Window_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (e.ButtonState == MouseButtonState.Pressed) DragMove();
    }
}
