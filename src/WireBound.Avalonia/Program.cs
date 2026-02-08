using Avalonia;
using Serilog;
using System;
using Velopack;

namespace WireBound.Avalonia;

class Program
{
    // Initialization code. Don't use any Avalonia, third-party APIs or any
    // SynchronizationContext-reliant code before AppMain is called: things aren't initialized
    // yet and stuff might break.
    [STAThread]
    public static void Main(string[] args)
    {
        // Velopack lifecycle hooks — MUST be the very first line.
        // Safe no-op when not installed via Velopack (portable/dev mode).
        VelopackApp.Build()
            .OnRestarted(v =>
            {
                // Flag for showing What's New dialog after update restart
                Environment.SetEnvironmentVariable("WIREBOUND_UPDATED_TO", v?.ToString());
            })
            .Run();

        // Configure Serilog early
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
                retainedFileCountLimit: 14,
                fileSizeLimitBytes: 10_485_760)
            .CreateLogger();

        Log.Information("WireBound Avalonia application starting...");

        try
        {
            BuildAvaloniaApp().StartWithClassicDesktopLifetime(args);
        }
        catch (Exception ex)
        {
            Log.Fatal(ex, "Application crashed");
            Console.Error.WriteLine($"FATAL: {ex}");
            throw;
        }
        finally
        {
            Log.CloseAndFlush();
        }
    }

    // Avalonia configuration, don't remove; also used by visual designer.
    public static AppBuilder BuildAvaloniaApp()
        => AppBuilder.Configure<App>()
            .UsePlatformDetect()
            .WithInterFont()
            .LogToTrace();
}
