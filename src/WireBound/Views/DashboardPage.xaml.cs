using Serilog;
using WireBound.ViewModels;

namespace WireBound.Views;

/// <summary>
/// Dashboard page showing real-time network monitoring stats.
/// Uses constructor injection for ViewModel - MAUI Shell resolves DI-registered pages automatically.
/// </summary>
public partial class DashboardPage : ContentPage
{
    public DashboardPage(DashboardViewModel viewModel)
    {
        Log.Information("DashboardPage constructor called");
        InitializeComponent();
        BindingContext = viewModel;
        Log.Information("DashboardPage initialized successfully");
    }
}
