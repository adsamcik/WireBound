using Serilog;
using WireBound.ViewModels;

namespace WireBound.Views;

/// <summary>
/// Applications page showing per-application network usage.
/// Uses constructor injection for ViewModel - MAUI Shell resolves DI-registered pages automatically.
/// </summary>
public partial class ApplicationsPage : ContentPage
{
    public ApplicationsPage(ApplicationsViewModel viewModel)
    {
        Log.Information("ApplicationsPage constructor called");
        InitializeComponent();
        BindingContext = viewModel;
        Log.Information("ApplicationsPage initialized successfully");
    }
}
