using CommunityToolkit.Maui;
using Microsoft.Extensions.Logging;
using Serilog;
using SkiaSharp.Views.Maui.Controls.Hosting;
using WireBound.Maui.Data;
using WireBound.Maui.Services;
using WireBound.Maui.ViewModels;
using WireBound.Maui.Views;

namespace WireBound.Maui;

public static class MauiProgram
{
    public static MauiApp CreateMauiApp()
    {
        // Configure Serilog
        var logPath = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "WireBound", "logs", "wirebound-.log");

        Log.Logger = new LoggerConfiguration()
            .MinimumLevel.Debug()
            .WriteTo.Debug()
            .WriteTo.File(logPath, rollingInterval: RollingInterval.Day)
            .CreateLogger();

        Log.Information("WireBound MAUI application starting...");

        var builder = MauiApp.CreateBuilder();
        builder
            .UseMauiApp<App>()
            .UseSkiaSharp()
            .UseMauiCommunityToolkit();

        // Add Serilog
        builder.Logging.AddSerilog(Log.Logger);

#if DEBUG
        builder.Logging.AddDebug();
#endif

        // Register Database
        builder.Services.AddDbContext<WireBoundDbContext>();

        // Register Services
        builder.Services.AddSingleton<INetworkMonitorService, NetworkMonitorService>();
        builder.Services.AddSingleton<IDataPersistenceService, DataPersistenceService>();
        builder.Services.AddSingleton<NetworkPollingBackgroundService>();
        builder.Services.AddSingleton<INetworkPollingBackgroundService>(sp => 
            sp.GetRequiredService<NetworkPollingBackgroundService>());
        // Note: NetworkPollingBackgroundService is started manually in App.xaml.cs
        // MAUI doesn't auto-start IHostedServices like ASP.NET Core does

        // Register ViewModels
        builder.Services.AddSingleton<DashboardViewModel>();
        builder.Services.AddSingleton<HistoryViewModel>();
        builder.Services.AddSingleton<SettingsViewModel>();
        builder.Services.AddSingleton<MainViewModel>();

        // Register Pages
        builder.Services.AddTransient<DashboardPage>();
        builder.Services.AddTransient<HistoryPage>();
        builder.Services.AddTransient<SettingsPage>();
        builder.Services.AddSingleton<MainPage>();
        builder.Services.AddSingleton<AppShell>();

        var app = builder.Build();

        // Initialize database
        try
        {
            using var scope = app.Services.CreateScope();
            var dbContext = scope.ServiceProvider.GetRequiredService<WireBoundDbContext>();
            dbContext.Database.EnsureCreated();
            Log.Information("Database initialized");
        }
        catch (Exception ex)
        {
            Log.Error(ex, "Failed to initialize database");
            throw; // Re-throw to prevent app from starting with broken database
        }

        return app;
    }
}
