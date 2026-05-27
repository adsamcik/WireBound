using Avalonia;
using Serilog;
using Serilog.Events;
using System;
using System.Threading;
using Velopack;

namespace WireBound.Avalonia;

class Program
{
    private const string MutexName = "WireBound-SingleInstance-A3F8D2E1";

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

        // Single-instance enforcement — exit immediately if another instance is running
        using var mutex = new Mutex(true, MutexName, out var createdNew);
        if (!createdNew)
        {
            Console.Error.WriteLine("WireBound is already running.");
            return;
        }

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
            // EF Core's Debug/Information channel emits one entry per query (including
            // the full SQL text). With our daily 10MB file cap that easily fills the log
            // within an hour, causing the file sink to silently drop later writes —
            // including the shutdown markers we rely on for diagnostics. Suppressing
            // anything below Warning keeps the noise out while preserving real issues.
            .MinimumLevel.Override("Microsoft.EntityFrameworkCore", LogEventLevel.Warning)
            .WriteTo.File(logPath,
                rollingInterval: RollingInterval.Day,
                retainedFileCountLimit: 14,
                fileSizeLimitBytes: 10_485_760,
                // Roll to a new file when the size cap is hit instead of silently
                // dropping every subsequent log event for the rest of the day.
                rollOnFileSizeLimit: true)
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
