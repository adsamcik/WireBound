using Microsoft.Extensions.Hosting;
using Serilog;
using WireBound.Services;
using WireBound.ViewModels;

namespace WireBound;

public partial class App : Application
{
    private readonly IServiceProvider _serviceProvider;
    private IHost? _host;
#if WINDOWS
    private ITrayIconService? _trayIconService;
    private bool _forceClose = false;
#endif
    
    /// <summary>
    /// Indicates whether background monitoring services are running.
    /// </summary>
    public bool IsMonitoringActive { get; private set; }

    public App(IServiceProvider serviceProvider)
    {
        InitializeComponent();
        _serviceProvider = serviceProvider;
    }

#if WINDOWS
    /// <summary>
    /// Force closes the application, bypassing minimize to tray.
    /// </summary>
    public void ForceQuit()
    {
        _forceClose = true;
        Quit();
    }
#endif

    protected override Window CreateWindow(IActivationState? activationState)
    {
        Log.Information("Creating main window...");

        // Start background services
        StartBackgroundServices();
        
        // Initialize system tray icon
        InitializeTrayIcon();

        // Use MAUI Shell for proper flyout navigation
        var shell = _serviceProvider.GetRequiredService<AppShell>();
        
        var window = new Window(shell)
        {
            Title = "WireBound - Network Traffic Monitor",
            Width = 1200,
            Height = 800,
            MinimumWidth = 900,
            MinimumHeight = 650
        };

        window.Destroying += OnWindowDestroying;
        
#if WINDOWS
        // Hook into window lifecycle for minimize-to-tray functionality
        window.HandlerChanged += (s, e) =>
        {
            if (window.Handler?.PlatformView is Microsoft.UI.Xaml.Window winUIWindow)
            {
                var hwnd = WinRT.Interop.WindowNative.GetWindowHandle(winUIWindow);
                var windowId = Microsoft.UI.Win32Interop.GetWindowIdFromWindow(hwnd);
                var appWindow = Microsoft.UI.Windowing.AppWindow.GetFromWindowId(windowId);
                
                appWindow.Closing += async (sender, args) =>
                {
                    if (!_forceClose)
                    {
                        // Check if minimize to tray is enabled
                        var persistence = _serviceProvider.GetRequiredService<IDataPersistenceService>();
                        var settings = await persistence.GetSettingsAsync();
                        
                        if (settings.MinimizeToTray)
                        {
                            args.Cancel = true;
                            _trayIconService?.HideMainWindow();
                            Log.Debug("Window hidden to system tray");
                        }
                    }
                };
            }
        };
#endif

        Log.Information("Main window created successfully");
        return window;
    }

    private void InitializeTrayIcon()
    {
#if WINDOWS
        try
        {
            _trayIconService = _serviceProvider.GetRequiredService<ITrayIconService>();
            Log.Information("System tray icon initialized");
        }
        catch (Exception ex)
        {
            Log.Error(ex, "Failed to initialize system tray icon");
        }
#endif
    }

    private void StartBackgroundServices()
    {
        try
        {
            // Get the singleton instance from MAUI container
            var pollingService = _serviceProvider.GetRequiredService<NetworkPollingBackgroundService>();
            
            // Create and start a minimal host for background services
            _host = Host.CreateDefaultBuilder()
                .UseSerilog()
                .ConfigureServices((context, services) =>
                {
                    // Re-use the existing services from MAUI container
                    var networkMonitor = _serviceProvider.GetRequiredService<INetworkMonitorService>();
                    var persistence = _serviceProvider.GetRequiredService<IDataPersistenceService>();
                    
                    services.AddSingleton(networkMonitor);
                    services.AddSingleton(persistence);
                    // Use the same instance from MAUI container so settings updates work
                    services.AddSingleton<IHostedService>(pollingService);
                })
                .Build();

            _host.Start();
            IsMonitoringActive = true;
            Log.Information("Background services started");
        }
        catch (Exception ex)
        {
            IsMonitoringActive = false;
            Log.Error(ex, "Failed to start background services");
            
            MainThread.BeginInvokeOnMainThread(async () =>
            {
                if (Application.Current?.Windows.Count > 0 && 
                    Application.Current.Windows[0].Page is Page page)
                {
                    await page.DisplayAlertAsync(
                        "Monitoring Error",
                        "Failed to start network monitoring. Please restart the application.",
                        "OK");
                }
            });
        }
    }

    private async void OnWindowDestroying(object? sender, EventArgs e)
    {
        try
        {
            Log.Information("Application shutting down...");

#if WINDOWS
            // Dispose tray icon
            _trayIconService?.Dispose();
            Log.Debug("Tray icon disposed");
#endif

            // Dispose ViewModels that have event subscriptions
            if (_serviceProvider.GetService<MainViewModel>() is IDisposable mainVm)
            {
                mainVm.Dispose();
                Log.Debug("MainViewModel disposed");
            }

            if (_host != null)
            {
                using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(5));
                await _host.StopAsync(cts.Token);
                _host.Dispose();
                Log.Debug("Background host stopped and disposed");
            }
        }
        catch (Exception ex)
        {
            Log.Error(ex, "Error during application shutdown");
        }
        finally
        {
            await Log.CloseAndFlushAsync();
        }
    }
}
