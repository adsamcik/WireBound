using Serilog;
using WireBound.ViewModels;

namespace WireBound.Views;

/// <summary>
/// History page showing network usage over time.
/// Uses constructor injection for ViewModel - MAUI Shell resolves DI-registered pages automatically.
/// </summary>
public partial class HistoryPage : ContentPage
{
    private readonly HistoryViewModel _viewModel;

    public HistoryPage(HistoryViewModel viewModel)
    {
        Log.Information("HistoryPage constructor called");
        InitializeComponent();
        _viewModel = viewModel;
        BindingContext = viewModel;
        Log.Information("HistoryPage initialized successfully");
    }

    protected override void OnAppearing()
    {
        base.OnAppearing();
        Log.Information("HistoryPage OnAppearing - refreshing data");
        
        // Refresh data every time the page appears
        if (_viewModel.RefreshCommand.CanExecute(null))
        {
            _viewModel.RefreshCommand.Execute(null);
        }
    }
}
