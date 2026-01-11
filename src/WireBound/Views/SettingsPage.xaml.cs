using Serilog;
using WireBound.ViewModels;

namespace WireBound.Views;

/// <summary>
/// Settings page for configuring monitoring options.
/// Uses constructor injection for ViewModel - MAUI Shell resolves DI-registered pages automatically.
/// </summary>
public partial class SettingsPage : ContentPage
{
    public SettingsPage(SettingsViewModel viewModel)
    {
        Log.Information("SettingsPage constructor called");
        InitializeComponent();
        BindingContext = viewModel;
        Log.Information("SettingsPage initialized successfully");
    }
}
