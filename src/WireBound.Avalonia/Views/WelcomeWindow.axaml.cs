using Avalonia.Controls;
using Avalonia.Interactivity;

namespace WireBound.Avalonia.Views;

public partial class WelcomeWindow : Window
{
    public WelcomeWindow()
    {
        InitializeComponent();
    }

    private void OnStartMonitoringClick(object? sender, RoutedEventArgs e)
    {
        Close();
    }
}
