using CommunityToolkit.Maui;
using LiveChartsCore.SkiaSharpView.Maui;
using Microsoft.Extensions.Logging;
using Serilog;
using SkiaSharp.Views.Maui.Controls.Hosting;
using WireBound.Core.Data;
using WireBound.Core.Services;
using WireBound.Services;
using WireBound.ViewModels;
using WireBound.Views;
#if WINDOWS
using WireBound.Windows;
#endif

namespace WireBound;

public static class MauiProgram
{
    public static MauiApp CreateMauiApp()
    {
        // Configure Serilog with appropriate log levels and retention
        var logPath = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "WireBound", "logs", "wirebound-.log");

        Log.Logger = new LoggerConfiguration()
#if DEBUG
            .MinimumLevel.Debug()
            .WriteTo.Debug()
#else
            .MinimumLevel.Information()
#endif
            .WriteTo.File(logPath, 
                rollingInterval: RollingInterval.Day,
                retainedFileCountLimit: 14,  // Keep 2 weeks of logs
                fileSizeLimitBytes: 10_485_760)  // 10 MB max per file
            .CreateLogger();

        Log.Information("WireBound MAUI application starting...");

        var builder = MauiApp.CreateBuilder();
        builder
            .UseMauiApp<App>()
            .UseSkiaSharp()
            .UseLiveCharts()  // Required for LiveCharts rc5+
            .UseMauiCommunityToolkit()
            .ConfigureHandlers();

        // Add Serilog
        builder.Logging.AddSerilog(Log.Logger);

#if DEBUG
        builder.Logging.AddDebug();
#endif

        // Register Database
        builder.Services.AddDbContext<WireBoundDbContext>();

        // Register platform-specific services
#if WINDOWS
        builder.Services.AddWindowsPlatformServices();
#endif
        
        // Register app-specific services
        builder.Services.AddSingleton<ILocalizationService, LocalizationService>();
        
        // Register NetworkPollingBackgroundService with both concrete type and interface
        // This allows accessing the same instance via either type
        builder.Services.AddSingleton<NetworkPollingBackgroundService>();
        builder.Services.AddSingleton<INetworkPollingBackgroundService>(static sp => 
            sp.GetRequiredService<NetworkPollingBackgroundService>());
        // Note: NetworkPollingBackgroundService is started manually in App.xaml.cs
        // MAUI doesn't auto-start IHostedServices like ASP.NET Core does

        // Register ViewModels as singletons for state persistence across navigation
        builder.Services.AddSingleton<DashboardViewModel>();
        builder.Services.AddSingleton<HistoryViewModel>();
        builder.Services.AddSingleton<SettingsViewModel>();
        builder.Services.AddSingleton<MainViewModel>();
        builder.Services.AddSingleton<ApplicationsViewModel>();

        // Register AppShell (singleton as it persists for app lifetime)
        builder.Services.AddSingleton<AppShell>();
        
        // Register Pages with DI for constructor-injected ViewModels (modern MAUI pattern)
        // This eliminates Service Locator anti-pattern and enables proper DI integration
        builder.Services.AddTransient<DashboardPage>();
        builder.Services.AddTransient<HistoryPage>();
        builder.Services.AddTransient<SettingsPage>();
        builder.Services.AddTransient<ChartsPage>();
        builder.Services.AddTransient<ApplicationsPage>();

        var app = builder.Build();

        // Initialize localization for static access (used by XAML markup extension)
        var localizationService = app.Services.GetRequiredService<ILocalizationService>();
        Strings.Initialize(localizationService);

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
    
    /// <summary>
    /// Configures platform-specific Handler customizations using modern MAUI mapper patterns.
    /// This approach centralizes platform tweaks without requiring custom renderers.
    /// </summary>
    private static MauiAppBuilder ConfigureHandlers(this MauiAppBuilder builder)
    {
        // Modern Handler customization pattern (.NET 10)
        // Uses Mapper.AppendToMapping for global control modifications
        
#if WINDOWS
        // Remove Entry underline on Windows for cleaner dark theme appearance
        Microsoft.Maui.Handlers.EntryHandler.Mapper.AppendToMapping("NoUnderline", (handler, view) =>
        {
            handler.PlatformView.BorderThickness = new Microsoft.UI.Xaml.Thickness(0);
            handler.PlatformView.Padding = new Microsoft.UI.Xaml.Thickness(8, 6, 8, 6);
        });
        
        // Remove Picker border for consistent dark theme styling
        Microsoft.Maui.Handlers.PickerHandler.Mapper.AppendToMapping("CleanPicker", (handler, view) =>
        {
            handler.PlatformView.BorderThickness = new Microsoft.UI.Xaml.Thickness(0);
        });
        
        // Remove Editor border for text areas
        Microsoft.Maui.Handlers.EditorHandler.Mapper.AppendToMapping("NoUnderline", (handler, view) =>
        {
            handler.PlatformView.BorderThickness = new Microsoft.UI.Xaml.Thickness(0);
        });
#endif
        
        return builder;
    }
}
