using System.Windows;
using System.Windows.Controls;
using Orbit.WindowsAgent;

namespace Orbit.Desktop;

public partial class WindowPicker : Window
{
    public WindowPicker(IReadOnlyList<ApplicationWindow> windows, bool thai)
    {
        InitializeComponent();
        if (thai)
        {
            Title = "เลือกหน้าต่าง";
            FocusButton.Content = "สลับหน้าต่าง";
        }
        Windows.ItemsSource = windows;
        Loaded += (_, _) => Windows.Focus();
    }

    public ApplicationWindow? Selection => Windows.SelectedItem as ApplicationWindow;

    private void Windows_SelectionChanged(object sender, SelectionChangedEventArgs e) =>
        FocusButton.IsEnabled = Selection is not null;

    private void Choose_Click(object sender, RoutedEventArgs e)
    {
        if (Selection is not null)
            DialogResult = true;
    }
}
