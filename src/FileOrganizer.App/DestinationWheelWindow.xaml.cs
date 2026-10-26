using System.Windows;
using System.Windows.Controls;

namespace FileOrganizer.App;

/// <summary>
/// Phase 2 compatibility shim. The old 320px wheel is replaced by the unified
/// WheelWindow; every call site now goes there directly, and this class only
/// forwards ShowWheel() so any remaining caller still works. The XAML window
/// itself is never shown any more.
/// </summary>
public partial class DestinationWheelWindow : Window
{
    public DestinationWheelWindow(List<string>? items = null)
    {
        InitializeComponent();
    }

    public static void ShowWheel(List<string>? items = null) => WheelWindow.ShowMove(items);

    private void Close_Click(object sender, RoutedEventArgs e) => Close();

    private void Wheel_Drop(object sender, DragEventArgs e) { /* never shown; WheelWindow owns drops now */ }
}
