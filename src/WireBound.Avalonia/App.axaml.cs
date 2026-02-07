using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Markup.Xaml;
using Avalonia.Threading;
using LiveChartsCore;
using LiveChartsCore.SkiaSharpView;
using Microsoft.Extensions.DependencyInjection;
using Serilog;
using WireBound.Avalonia.Services;
using WireBound.Avalonia.ViewModels;
using WireBound.Avalonia.Views;
using WireBound.Core.Data;
using WireBound.Core.Services;
using WireBound.Platform.Abstract.Services;
using WireBound.Platform.Linux;
using WireBound.Platform.Stub;
using WireBound.Platform.Windows;

namespace WireBound.Avalonia;

public partial class App : Application
{
    private ServiceProvider? _serviceProvider;

    public override void Initialize()
    {
        AvaloniaXamlLoader.Load(this);

        // Configure LiveCharts
        LiveCharts.Configure(config => config
            .AddSkiaSharp()
            .AddDefaultMappers());
    }

    public override void OnFrameworkInitializationCompleted()
    {
        // Build dependency injection container
        var services = new ServiceCollection();
        ConfigureServices(services);
        _serviceProvider = services.BuildServiceProvider();

        // Initialize static localization accessor
        var localizationService = _serviceProvider.GetRequiredService<ILocalizationService>();
        Strings.Initialize(localizationService);

        // Initialize database (synchronous, fast operation)
        InitializeDatabase();

        if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
        {
            // Create main window with navigation
            var mainViewModel = _serviceProvider.GetRequiredService<MainViewModel>();
            var mainWindow = new MainWindow
            {
                DataContext = mainViewModel
            };
            desktop.MainWindow = mainWindow;

            desktop.ShutdownRequested += OnShutdownRequested;

            // Fire and forget async initialization - avoids blocking UI thread
            _ = InitializeAsyncServicesAsync(mainWindow);

            Log.Information("WireBound Avalonia application started successfully");
        }

        base.OnFrameworkInitializationCompleted();
    }

    /// <summary>
    /// Handles all async initialization to avoid blocking the UI thread.
    /// Uses fire-and-forget pattern with proper error handling.
    /// </summary>
    private async Task InitializeAsyncServicesAsync(MainWindow mainWindow)
    {
        try
        {
            // Ensure startup entry points to current executable (handles updates that change install path)
            await EnsureStartupPathUpdatedAsync();

            // Initialize tray icon and apply settings
            await InitializeTrayIconAsync(mainWindow);

            // Apply saved theme preference
            await ApplyThemeFromSettingsAsync();

            // Check if we should start minimized
            await ApplyStartMinimizedSettingAsync(mainWindow);

            // Start background services
            await StartBackgroundServicesAsync();

            // Check for updates (non-blocking, privacy-safe)
            await CheckForUpdatesAsync();
        }
        catch (Exception ex)
        {
            Log.Error(ex, "Failed to complete async initialization");
        }
    }

    private void ConfigureServices(IServiceCollection services)
    {
        // Register logging (using Microsoft.Extensions.Logging)
        services.AddLogging();

        // Register Database
        services.AddDbContext<WireBoundDbContext>();

        // Register cross-platform network monitoring service
        // Uses System.Net.NetworkInformation which works on Windows and Linux
        services.AddSingleton<INetworkMonitorService, CrossPlatformNetworkMonitorService>();

        // Register data persistence with segregated interfaces for ISP compliance
        // The DataPersistenceService implements all focused repository interfaces
        services.AddSingleton<DataPersistenceService>();
        services.AddSingleton<IDataPersistenceService>(sp => sp.GetRequiredService<DataPersistenceService>());
        services.AddSingleton<INetworkUsageRepository>(sp => sp.GetRequiredService<DataPersistenceService>());
        services.AddSingleton<IAppUsageRepository>(sp => sp.GetRequiredService<DataPersistenceService>());
        services.AddSingleton<ISettingsRepository>(sp => sp.GetRequiredService<DataPersistenceService>());
        services.AddSingleton<ISpeedSnapshotRepository>(sp => sp.GetRequiredService<DataPersistenceService>());

        services.AddSingleton<IWiFiInfoService, WiFiInfoService>();

        // Register platform services (stub first, then override with platform-specific)
        StubPlatformServices.Instance.Register(services);

        if (OperatingSystem.IsWindows())
        {
            WindowsPlatformServices.Instance.Register(services);
        }
        else if (OperatingSystem.IsLinux())
        {
            LinuxPlatformServices.Instance.Register(services);
        }

        // Register per-app network tracking service (adapts platform providers)
        services.AddSingleton<IProcessNetworkService, ProcessNetworkService>();

        // Register system monitoring service (CPU, RAM)
        services.AddSingleton<ISystemMonitorService, SystemMonitorService>();

        // Register system history service for historical stats tracking
        services.AddSingleton<ISystemHistoryService, SystemHistoryService>();

        // Register DNS resolver service for reverse lookups
        services.AddSingleton<IDnsResolverService, DnsResolverService>();

        // Register data export service
        services.AddSingleton<IDataExportService, DataExportService>();

        // Register update check service
        services.AddSingleton<IUpdateService, GitHubUpdateService>();

        // Register app-specific services
        services.AddSingleton<INavigationService, NavigationService>();
        services.AddSingleton<ILocalizationService, LocalizationService>();
        services.AddSingleton<IViewFactory, ViewFactory>();
        services.AddSingleton<ITrayIconService, TrayIconService>();

        // Register background service
        services.AddSingleton<NetworkPollingBackgroundService>();
        services.AddSingleton<INetworkPollingBackgroundService>(sp =>
            sp.GetRequiredService<NetworkPollingBackgroundService>());

        // Register ViewModels
        services.AddSingleton<MainViewModel>();
        services.AddSingleton<OverviewViewModel>();
        services.AddSingleton<ChartsViewModel>();
        services.AddSingleton<InsightsViewModel>();
        services.AddSingleton<SettingsViewModel>();
        services.AddSingleton<ApplicationsViewModel>();
        services.AddSingleton<ConnectionsViewModel>();
        services.AddSingleton<SystemViewModel>();

        // Register View factory for navigation
        services.AddTransient<OverviewView>();
        services.AddTransient<ChartsView>();
        services.AddTransient<InsightsView>();
        services.AddTransient<SettingsView>();
        services.AddTransient<ApplicationsView>();
        services.AddTransient<ConnectionsView>();
        services.AddTransient<SystemView>();
    }

    private void InitializeDatabase()
    {
        try
        {
            using var scope = _serviceProvider!.CreateScope();
            var context = scope.ServiceProvider.GetRequiredService<WireBoundDbContext>();
            context.Database.EnsureCreated();
            context.ApplyMigrations();
            Log.Information("Database initialized successfully");
        }
        catch (Exception ex)
        {
            Log.Error(ex, "Failed to initialize database");
        }
    }

    private async Task EnsureStartupPathUpdatedAsync()
    {
        try
        {
            var startupService = _serviceProvider!.GetRequiredService<IStartupService>();
            if (!startupService.IsStartupSupported)
            {
                return;
            }

            var result = await startupService.EnsureStartupPathUpdatedAsync();
            if (!result)
            {
                Log.Warning("Failed to ensure startup path is updated");
            }
        }
        catch (Exception ex)
        {
            Log.Error(ex, "Failed to ensure startup path is updated");
        }
    }

    private async Task StartBackgroundServicesAsync()
    {
        try
        {
            var pollingService = _serviceProvider!.GetRequiredService<NetworkPollingBackgroundService>();
            await pollingService.StartAsync(CancellationToken.None);
            Log.Information("Background polling service started");
        }
        catch (Exception ex)
        {
            Log.Error(ex, "Failed to start background services");
        }
    }

    private async Task InitializeTrayIconAsync(MainWindow mainWindow)
    {
        if (_serviceProvider is null) return;

        try
        {
            // Load settings to get minimize to tray preference
            var persistence = _serviceProvider.GetRequiredService<IDataPersistenceService>();
            var settings = await persistence.GetSettingsAsync();

            var trayIconService = (TrayIconService)_serviceProvider.GetRequiredService<ITrayIconService>();

            // Tray icon initialization may touch UI, ensure we're on UI thread
            await Dispatcher.UIThread.InvokeAsync(() =>
            {
                trayIconService.Initialize(mainWindow, settings.MinimizeToTray);
            });

            // Subscribe to settings changes
            var settingsViewModel = _serviceProvider.GetRequiredService<SettingsViewModel>();
            settingsViewModel.PropertyChanged += (_, e) =>
            {
                if (e.PropertyName == nameof(SettingsViewModel.MinimizeToTray))
                {
                    trayIconService.MinimizeToTray = settingsViewModel.MinimizeToTray;
                }
            };

            Log.Information("Tray icon initialized with MinimizeToTray={MinimizeToTray}", settings.MinimizeToTray);
        }
        catch (Exception ex)
        {
            Log.Error(ex, "Failed to initialize tray icon");
        }
    }

    private async Task ApplyThemeFromSettingsAsync()
    {
        if (_serviceProvider is null) return;

        try
        {
            var persistence = _serviceProvider.GetRequiredService<IDataPersistenceService>();
            var settings = await persistence.GetSettingsAsync();
            await Dispatcher.UIThread.InvokeAsync(() =>
                Helpers.ThemeHelper.ApplyTheme(settings.Theme));
            Log.Information("Applied theme: {Theme}", settings.Theme);
        }
        catch (Exception ex)
        {
            Log.Error(ex, "Failed to apply theme from settings");
        }
    }

    private async Task ApplyStartMinimizedSettingAsync(MainWindow mainWindow)
    {
        if (_serviceProvider is null) return;

        try
        {
            var persistence = _serviceProvider.GetRequiredService<IDataPersistenceService>();
            var settings = await persistence.GetSettingsAsync();

            if (settings.StartMinimized)
            {
                // Use the tray service to hide properly (handles cross-platform differences)
                // UI operations must be on UI thread
                await Dispatcher.UIThread.InvokeAsync(() =>
                {
                    var trayService = _serviceProvider.GetRequiredService<ITrayIconService>();
                    trayService.HideMainWindow();
                });

                Log.Information("Application started minimized to tray");
            }
        }
        catch (Exception ex)
        {
            Log.Error(ex, "Failed to apply start minimized setting");
        }
    }

    private async Task CheckForUpdatesAsync()
    {
        try
        {
            var persistence = _serviceProvider!.GetRequiredService<IDataPersistenceService>();
            var settings = await persistence.GetSettingsAsync();
            if (!settings.CheckForUpdates) return;

            var updateService = _serviceProvider!.GetRequiredService<IUpdateService>();
            var update = await updateService.CheckForUpdateAsync();
            if (update is not null)
            {
                var settingsVm = _serviceProvider!.GetRequiredService<SettingsViewModel>();
                settingsVm.UpdateAvailable = true;
                settingsVm.LatestVersion = update.Version;
                settingsVm.UpdateUrl = update.DownloadUrl;
                Log.Information("Update available: {Version}", update.Version);
            }
        }
        catch (Exception ex)
        {
            Log.Warning(ex, "Update check failed");
        }
    }

    private void OnShutdownRequested(object? sender, ShutdownRequestedEventArgs e)
    {
        Log.Information("Application shutting down...");

        try
        {
            // Dispose tray icon service
            _serviceProvider?.GetService<ITrayIconService>()?.Dispose();

            // Stop background services - use async void pattern for shutdown
            // This is acceptable here as the app is terminating
            var pollingService = _serviceProvider?.GetService<NetworkPollingBackgroundService>();
            if (pollingService is not null)
            {
                // Fire and forget with a reasonable timeout
                _ = StopBackgroundServicesAsync(pollingService);
            }
        }
        catch (Exception ex)
        {
            Log.Error(ex, "Error stopping background services");
        }

        // Dispose ViewModels that implement IDisposable
        DisposeViewModels();

        _serviceProvider?.Dispose();
    }

    private static async Task StopBackgroundServicesAsync(NetworkPollingBackgroundService pollingService)
    {
        try
        {
            using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(5));
            await pollingService.StopAsync(cts.Token);
        }
        catch (OperationCanceledException)
        {
            Log.Warning("Background service stop timed out during shutdown");
        }
        catch (Exception ex)
        {
            Log.Error(ex, "Error stopping background services during shutdown");
        }
    }

    private void DisposeViewModels()
    {
        try
        {
            // Dispose all ViewModels that implement IDisposable
            // This ensures event handlers are unsubscribed and resources are released
            _serviceProvider?.GetService<MainViewModel>()?.Dispose();
            _serviceProvider?.GetService<OverviewViewModel>()?.Dispose();
            _serviceProvider?.GetService<ChartsViewModel>()?.Dispose();
            _serviceProvider?.GetService<InsightsViewModel>()?.Dispose();
            _serviceProvider?.GetService<SystemViewModel>()?.Dispose();
            _serviceProvider?.GetService<ConnectionsViewModel>()?.Dispose();
            _serviceProvider?.GetService<SettingsViewModel>()?.Dispose();
            _serviceProvider?.GetService<ApplicationsViewModel>()?.Dispose();

            Log.Information("ViewModels disposed successfully");
        }
        catch (Exception ex)
        {
            Log.Error(ex, "Error disposing ViewModels");
        }
    }
}
