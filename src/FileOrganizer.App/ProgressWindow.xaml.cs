using System.Windows;

namespace FileOrganizer.App;

/// <summary>Progress + Cancel dialog for long transfers (recommendation 4). Items already done stay done; Undo reverses them.</summary>
public partial class ProgressWindow : Window
{
    public CancellationTokenSource Cts { get; } = new();

    public ProgressWindow(string title)
    {
        InitializeComponent();
        TitleText.Text = title;
    }

    /// <summary>Creates a Progress reporter wired to this window (call on the UI thread so Progress captures the right context).</summary>
    public IProgress<(int Done, int Total, string CurrentFile)> CreateProgress() =>
        new Progress<(int Done, int Total, string CurrentFile)>(p =>
        {
            var total = Math.Max(1, p.Total);
            Bar.Value = p.Total == 0 ? 0 : p.Done * 100.0 / total;
            DetailText.Text = p.Total == 0
                ? p.CurrentFile
                : $"{p.Done} / {p.Total} — {p.CurrentFile}";
        });

    private void Cancel_Click(object sender, RoutedEventArgs e)
    {
        DetailText.Text = "Cancelling after the current item…";
        Cts.Cancel();
    }

    protected override void OnClosed(EventArgs e)
    {
        Cts.Dispose();
        base.OnClosed(e);
    }
}
