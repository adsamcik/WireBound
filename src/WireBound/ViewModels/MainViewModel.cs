using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace WireBound.ViewModels;

public partial class MainViewModel : ObservableObject, IDisposable
{
    [ObservableProperty]
    private ObservableObject? _currentView;

    [ObservableProperty]
    private int _selectedNavigationIndex = 0;

    private readonly DashboardViewModel _dashboardViewModel;
    private readonly HistoryViewModel _historyViewModel;
    private readonly SettingsViewModel _settingsViewModel;
    private bool _disposed;

    public MainViewModel(
        DashboardViewModel dashboardViewModel,
        HistoryViewModel historyViewModel,
        SettingsViewModel settingsViewModel)
    {
        _dashboardViewModel = dashboardViewModel;
        _historyViewModel = historyViewModel;
        _settingsViewModel = settingsViewModel;

        CurrentView = _dashboardViewModel;
    }

    partial void OnSelectedNavigationIndexChanged(int value)
    {
        CurrentView = value switch
        {
            0 => _dashboardViewModel,
            1 => _historyViewModel,
            2 => _settingsViewModel,
            _ => _dashboardViewModel
        };
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
        }

        _disposed = true;
    }
}
