using Avalonia;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Markup.Xaml;
using LiveChartsCore;
using LiveChartsCore.SkiaSharpView;
using Microsoft.Extensions.DependencyInjection;
using Serilog;
using WireBound.Avalonia.Services;
using WireBound.Avalonia.ViewModels;
using WireBound.Avalonia.Views;
using WireBound.Core.Data;
using WireBound.Core.Services;

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

        // Initialize database
        InitializeDatabase();

        if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
        {
            // Create main window with navigation
            var mainViewModel = _serviceProvider.GetRequiredService<MainViewModel>();
            desktop.MainWindow = new MainWindow
            {
                DataContext = mainViewModel
            };

            desktop.ShutdownRequested += OnShutdownRequested;

            // Start background services
            StartBackgroundServices();

            Log.Information("WireBound Avalonia application started successfully");
        }

        base.OnFrameworkInitializationCompleted();
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
        services.AddSingleton<IDataPersistenceService, DataPersistenceService>();
        services.AddSingleton<IWiFiInfoService, WiFiInfoService>();

        // Register app-specific services
        services.AddSingleton<INavigationService, NavigationService>();
        services.AddSingleton<ILocalizationService, LocalizationService>();

        // Register background service
        services.AddSingleton<NetworkPollingBackgroundService>();
        services.AddSingleton<INetworkPollingBackgroundService>(sp =>
            sp.GetRequiredService<NetworkPollingBackgroundService>());

        // Register ViewModels
        services.AddSingleton<MainViewModel>();
        services.AddSingleton<DashboardViewModel>();
        services.AddSingleton<ChartsViewModel>();
        services.AddSingleton<HistoryViewModel>();
        services.AddSingleton<SettingsViewModel>();
        services.AddSingleton<ApplicationsViewModel>();

        // Register View factory for navigation
        services.AddTransient<DashboardView>();
        services.AddTransient<ChartsView>();
        services.AddTransient<HistoryView>();
        services.AddTransient<SettingsView>();
        services.AddTransient<ApplicationsView>();
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

    private void StartBackgroundServices()
    {
        try
        {
            var pollingService = _serviceProvider!.GetRequiredService<NetworkPollingBackgroundService>();
            _ = pollingService.StartAsync(CancellationToken.None);
            Log.Information("Background polling service started");
        }
        catch (Exception ex)
        {
            Log.Error(ex, "Failed to start background services");
        }
    }

    private void OnShutdownRequested(object? sender, ShutdownRequestedEventArgs e)
    {
        Log.Information("Application shutting down...");

        try
        {
            var pollingService = _serviceProvider?.GetService<NetworkPollingBackgroundService>();
            pollingService?.StopAsync(CancellationToken.None).GetAwaiter().GetResult();
        }
        catch (Exception ex)
        {
            Log.Error(ex, "Error stopping background services");
        }

        _serviceProvider?.Dispose();
    }
}
