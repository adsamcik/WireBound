using Avalonia;
using Serilog;
using System;
using System.Runtime.Versioning;
using System.Threading;
using Velopack;
using WireBound.Platform.Windows.Services;

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
        var velopackApp = VelopackApp.Build()
            .OnRestarted(v =>
            {
                // Flag for showing What's New dialog after update restart
                Environment.SetEnvironmentVariable("WIREBOUND_UPDATED_TO", v?.ToString());
            });

        // OnBeforeUninstallFastCallback is Windows-only (fast-exit hook, never invoked on
        // other platforms) — guard it so the CA1416 platform-compatibility analyzer is satisfied.
        if (OperatingSystem.IsWindows())
        {
            velopackApp.OnBeforeUninstallFastCallback(_ => CleanupWindowsStartupArtifacts());
        }

        velopackApp.Run();

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

    /// <summary>
    /// Removes OS-level artifacts (Registry startup entry, elevation helper scheduled task)
    /// left behind by <c>WindowsStartupService</c> before Add/Remove Programs uninstalls the app.
    /// </summary>
    /// <remarks>
    /// Runs as a Velopack fast-exit hook (before <see cref="Environment.Exit(int)"/> is called),
    /// so it must complete quickly and must never throw — a cleanup failure should never block
    /// or fail the uninstall. Serilog is not configured yet at this point in the process lifetime,
    /// so failures here are swallowed silently rather than logged.
    /// </remarks>
    [SupportedOSPlatform("windows")]
    private static void CleanupWindowsStartupArtifacts()
    {
        var startupService = new WindowsStartupService();

        try
        {
            startupService.SetStartupEnabledAsync(false).GetAwaiter().GetResult();
        }
        catch
        {
            // Best-effort cleanup — never block uninstall.
        }

        try
        {
            startupService.SetHelperStartupEnabledAsync(false).GetAwaiter().GetResult();
        }
        catch
        {
            // Best-effort cleanup — never block uninstall.
        }
    }
}
