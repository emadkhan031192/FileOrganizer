using System.Windows;
using System.Windows.Threading;

namespace FileOrganizer.App;

/// <summary>v2 feedback: a small toast ("Moved 12 files to Jobs — Undo") instead of only the status bar.
/// Auto-dismisses after 6 seconds; Undo reverses the batch it reports.</summary>
public partial class ToastWindow : Window
{
    private readonly Action? _onUndo;

    private ToastWindow(string message, Action? onUndo)
    {
        InitializeComponent();
        MessageText.Text = message;
        _onUndo = onUndo;
        UndoBtn.Visibility = onUndo is null ? Visibility.Collapsed : Visibility.Visible;
        Loaded += (_, _) =>
        {
            Left = SystemParameters.WorkArea.Right - ActualWidth - 16;
            Top = SystemParameters.WorkArea.Bottom - ActualHeight - 16;
        };
        var timer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(6) };
        timer.Tick += (_, _) => { timer.Stop(); Close(); };
        timer.Start();
    }

    public static void Show(string message, Action? onUndo = null) => new ToastWindow(message, onUndo).Show();

    private void Undo_Click(object sender, RoutedEventArgs e) { _onUndo?.Invoke(); Close(); }

    private void Close_Click(object sender, RoutedEventArgs e) => Close();
}
