using System.Windows;
using FileOrganizer.Core;

namespace FileOrganizer.App;

/// <summary>§10 ORGANIZATION PREVIEW: the explicit confirmation step before any bulk move.</summary>
public partial class PreviewWindow : Window
{
    public List<PreviewItem> ConfirmedItems { get; private set; } = new();

    public PreviewWindow(List<PreviewItem> items)
    {
        InitializeComponent();
        Grid.ItemsSource = items;
        var conflicts = items.Count(i => i.HasConflict);
        SummaryText.Text = $"{items.Count} file(s)" + (conflicts > 0
            ? $" · {conflicts} name conflict(s) in the destination (handled per your If-exists setting)"
            : "");
    }

    private void Organize_Click(object sender, RoutedEventArgs e)
    {
        ConfirmedItems = ((List<PreviewItem>)Grid.ItemsSource).Where(i => i.Included).ToList();
        DialogResult = true;
        Close();
    }
}
