using System.Windows;
using FileOrganizer.Core;

namespace FileOrganizer.App;

/// <summary>v2 (recommendation 6): Undo is no longer just "last action" — every batch is listed
/// here and any of them can be reversed.</summary>
public partial class UndoHistoryWindow : Window
{
    public UndoHistoryWindow()
    {
        InitializeComponent();
        RefreshList();
    }

    private void RefreshList()
    {
        BatchesList.ItemsSource = App.State.History.Batches(30)
            .Select(b => new BatchRow(b)).ToList();
        BatchesList.DisplayMemberPath = "Text";
    }

    private sealed record BatchRow(HistoryService.HistoryBatch Batch)
    {
        public string Text => $"{(Batch.Undone ? "↩ undone · " : "")}{Batch.Kind} · {Batch.FileCount} file(s) · {Batch.TimestampUtc.ToLocalTime():yyyy-MM-dd HH:mm}";
    }

    private async void Undo_Click(object sender, RoutedEventArgs e)
    {
        if (BatchesList.SelectedItem is not BatchRow row || row.Batch.Undone) return;
        var result = await Task.Run(() => App.State.Ops.UndoBatch(row.Batch.BatchId));
        MessageBox.Show($"Undid {result.Succeeded} file(s)." +
            (result.Failed > 0 ? $"\n{result.Failed} could not be reversed." : ""),
            "File Organizer", MessageBoxButton.OK, MessageBoxImage.Information);
        RefreshList();
        if (Application.Current.MainWindow is MainWindow main)
            main.RefreshActivity();
    }
}
