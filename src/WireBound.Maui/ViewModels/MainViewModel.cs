using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace WireBound.Maui.ViewModels;

public partial class MainViewModel : ObservableObject, IDisposable
{
    [ObservableProperty]
    public partial int SelectedNavigationIndex { get; set; }

    private readonly DashboardViewModel _dashboardViewModel;
    private readonly HistoryViewModel _historyViewModel;
    private readonly SettingsViewModel _settingsViewModel;
    private bool _disposed;

    public DashboardViewModel DashboardViewModel => _dashboardViewModel;
    public HistoryViewModel HistoryViewModel => _historyViewModel;
    public SettingsViewModel SettingsViewModel => _settingsViewModel;

    public MainViewModel(
        DashboardViewModel dashboardViewModel,
        HistoryViewModel historyViewModel,
        SettingsViewModel settingsViewModel)
    {
        _dashboardViewModel = dashboardViewModel;
        _historyViewModel = historyViewModel;
        _settingsViewModel = settingsViewModel;
    }

    [RelayCommand]
    private void NavigateToDashboard() => SelectedNavigationIndex = 0;

    [RelayCommand]
    private void NavigateToHistory() => SelectedNavigationIndex = 1;

    [RelayCommand]
    private void NavigateToSettings() => SelectedNavigationIndex = 2;

    public void Dispose()
    {
        Dispose(true);
        GC.SuppressFinalize(this);
    }

    protected virtual void Dispose(bool disposing)
    {
        if (_disposed) return;

        if (disposing)
        {
            _dashboardViewModel.Dispose();
            _historyViewModel.Dispose();
            _settingsViewModel.Dispose();
        }

        _disposed = true;
    }
}
