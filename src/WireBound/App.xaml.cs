using System.IO;
using System.Windows;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Serilog;
using WireBound.Data;
using WireBound.Services;
using WireBound.ViewModels;
using WireBound.Views;

namespace WireBound;

public partial class App : Application
{
    private IHost? _host;

    public static IServiceProvider Services { get; private set; } = null!;

    private void Application_Startup(object sender, StartupEventArgs e)
    {
        var logPath = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "WireBound", "logs", "wirebound-.log");

        Log.Logger = new LoggerConfiguration()
            .MinimumLevel.Debug()
            .WriteTo.Debug()
            .WriteTo.File(logPath, rollingInterval: RollingInterval.Day)
            .CreateLogger();

        Log.Information("WireBound application starting...");

        try
        {
            _host = Host.CreateDefaultBuilder()
                .UseSerilog()
                .ConfigureServices((context, services) =>
                {
                    // Database
                    services.AddDbContext<WireBoundDbContext>();

                    // Services
                    services.AddSingleton<INetworkMonitorService, NetworkMonitorService>();
                    services.AddSingleton<IDataPersistenceService, DataPersistenceService>();
                    services.AddSingleton<ITelemetryService, TelemetryService>();
                    services.AddHostedService<NetworkPollingBackgroundService>();

                    // ViewModels
                    services.AddSingleton<DashboardViewModel>(sp => new DashboardViewModel(
                        sp.GetRequiredService<INetworkMonitorService>(),
                        sp.GetRequiredService<IDataPersistenceService>(),
                        sp));
                    services.AddSingleton<HistoryViewModel>();
                    services.AddSingleton<SettingsViewModel>();
                    services.AddSingleton<MainViewModel>();

                    // MainWindow
                    services.AddSingleton<MainWindow>();
                })
                .Build();

            Services = _host.Services;
            Log.Information("Host built successfully");

            // Ensure database is created
            using var scope = Services.CreateScope();
            var dbContext = scope.ServiceProvider.GetRequiredService<WireBoundDbContext>();
            dbContext.Database.EnsureCreated();
            Log.Information("Database initialized");

            // Start background services
            _host.Start();
            Log.Information("Background services started");

            // Show main window
            Log.Information("Creating MainWindow...");
            var mainWindow = Services.GetRequiredService<MainWindow>();
            Log.Information("MainWindow created, calling Show()...");
            
            mainWindow.Show();
            Log.Information("Show() called, window should be visible");
            
            mainWindow.Activate();
            mainWindow.Focus();
            Log.Information("Window activated and focused");
        }
        catch (Exception ex)
        {
            Log.Fatal(ex, "Fatal error during application startup");
            MessageBox.Show($"Error starting WireBound:\n{ex.Message}\n\n{ex.StackTrace}", 
                "Startup Error", MessageBoxButton.OK, MessageBoxImage.Error);
            Shutdown(1);
        }
    }

    private async void Application_Exit(object sender, ExitEventArgs e)
    {
        if (_host is not null)
        {
            // Dispose ViewModels that have event subscriptions
            var mainViewModel = Services.GetService<MainViewModel>();
            mainViewModel?.Dispose();

            await _host.StopAsync();
            _host.Dispose();
        }

        await Log.CloseAndFlushAsync();
    }
}
