using System.Windows;

namespace Orbit.Desktop;

public partial class InspectionWindow : Window
{
    internal InspectionWindow(string title, WindowInspection inspection)
    {
        InitializeComponent();
        Title = $"UI inspection — {title}";
        Summary.Text = inspection.ControlCount == 0
            ? "Windows UI Automation exposed no child controls for this window."
            : $"{inspection.ControlCount} controls" + (inspection.Truncated ? " (partial tree)" : "");
        Outline.Text = inspection.Outline;
    }
}
