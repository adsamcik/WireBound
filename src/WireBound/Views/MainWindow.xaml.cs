using System.ComponentModel;
using System.Windows;
using H.NotifyIcon;
using WireBound.Models;
using WireBound.Services;
using WireBound.ViewModels;

namespace WireBound.Views;

public partial class MainWindow : Window
{
    private readonly IDataPersistenceService _persistenceService;
    private readonly INetworkMonitorService _networkMonitorService;
    private TaskbarIcon? _trayIcon;
    private AppSettings _settings = new();
    private bool _isExiting = false;

    public MainWindow(MainViewModel viewModel, IDataPersistenceService persistenceService, INetworkMonitorService networkMonitorService)
    {
        InitializeComponent();
        DataContext = viewModel;
        _persistenceService = persistenceService;
        _networkMonitorService = networkMonitorService;
        
        Loaded += MainWindow_Loaded;
        
        // Subscribe to network stats updates for tray tooltip
        _networkMonitorService.StatsUpdated += OnNetworkStatsUpdated;
    }

    private async void MainWindow_Loaded(object sender, RoutedEventArgs e)
    {
        // Get the TaskbarIcon from resources (now that the window is loaded)
        if (Resources.Contains("TrayIcon"))
        {
            _trayIcon = (TaskbarIcon)Resources["TrayIcon"];
        }
        
        // Load settings to check MinimizeToTray preference
        _settings = await _persistenceService.GetSettingsAsync();
    }

    private void OnNetworkStatsUpdated(object? sender, NetworkStats stats)
    {
        // Update tray tooltip with current speeds
        Dispatcher.Invoke(() =>
        {
            if (_trayIcon is null) return;
            
            var downloadSpeed = FormatSpeed(stats.DownloadSpeedBps);
            var uploadSpeed = FormatSpeed(stats.UploadSpeedBps);
            _trayIcon.ToolTipText = $"WireBound\n↓ {downloadSpeed}  ↑ {uploadSpeed}";
        });
    }

    private static string FormatSpeed(double bytesPerSecond)
    {
        return bytesPerSecond switch
        {
            >= 1_073_741_824 => $"{bytesPerSecond / 1_073_741_824:F2} GB/s",
            >= 1_048_576 => $"{bytesPerSecond / 1_048_576:F2} MB/s",
            >= 1024 => $"{bytesPerSecond / 1024:F2} KB/s",
            _ => $"{bytesPerSecond:F0} B/s"
        };
    }

    private void Window_StateChanged(object sender, EventArgs e)
    {
        // Minimize to tray if setting is enabled
        if (WindowState == WindowState.Minimized && _settings.MinimizeToTray)
        {
            Hide();
        }
    }

    private void Window_Closing(object sender, CancelEventArgs e)
    {
        // If not exiting via tray menu, minimize to tray instead of closing (if enabled)
        if (!_isExiting && _settings.MinimizeToTray)
        {
            e.Cancel = true;
            Hide();
            return;
        }

        // Clean up tray icon
        _networkMonitorService.StatsUpdated -= OnNetworkStatsUpdated;
        _trayIcon?.Dispose();
    }

    private void TrayIcon_TrayLeftMouseDoubleClick(object sender, RoutedEventArgs e)
    {
        ShowAndActivateWindow();
    }

    private void TrayMenuItem_Show_Click(object sender, RoutedEventArgs e)
    {
        ShowAndActivateWindow();
    }

    private void TrayMenuItem_Hide_Click(object sender, RoutedEventArgs e)
    {
        Hide();
    }

    private void TrayMenuItem_Exit_Click(object sender, RoutedEventArgs e)
    {
        _isExiting = true;
        Application.Current.Shutdown();
    }

    private void ShowAndActivateWindow()
    {
        Show();
        WindowState = WindowState.Normal;
        Activate();
    }
}
