using System.Windows;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Animation;

namespace Orbit.Desktop;

public partial class PetWindow : Window
{
    private readonly Action _showOrbit;

    public PetWindow(Action showOrbit)
    {
        InitializeComponent();
        _showOrbit = showOrbit;
        Left = SystemParameters.WorkArea.Right - Width - 20;
        Top = SystemParameters.WorkArea.Bottom - Height - 20;
    }

    public void SetState(string state)
    {
        Status.Text = state;
        Pulse.BeginAnimation(ScaleTransform.ScaleYProperty,
            new DoubleAnimation(1, state == "Idle" ? 1.06 : 1.16, new Duration(TimeSpan.FromMilliseconds(500)))
            {
                AutoReverse = true,
                RepeatBehavior = RepeatBehavior.Forever
            });
    }

    private void MovePet(object sender, MouseButtonEventArgs e) => DragMove();
    private void ShowOrbit(object sender, RoutedEventArgs e) => _showOrbit();
    private void ClosePet(object sender, RoutedEventArgs e) => Close();
}
