using Avalonia;
using LiveChartsCore.SkiaSharpView.Avalonia;
using Serilog;
using Serilog.Events;
using System;
using System.Runtime.InteropServices;
using System.Runtime.Versioning;
using System.Threading;
using System.Threading.Tasks;
using Velopack;
using WireBound.Platform.Abstract.Helpers;
using WireBound.Platform.Windows.Services;

namespace WireBound.Avalonia;

class Program
{
    private const string MutexName = "WireBound-SingleInstance-A3F8D2E1";
    private const string StartupCheckArgument = "--startup-check";
    private const uint ErrorMessageBox = 0x00000010;

    // Initialization code. Don't use any Avalonia, third-party APIs or any
    // SynchronizationContext-reliant code before AppMain is called: things aren't initialized
    // yet and stuff might break.
    [STAThread]
    public static void Main(string[] args)
    {
        // Velopack lifecycle hooks — MUST be the very first line.
        // Safe no-op when not installed via Velopack (portable/dev mode).
        var velopackApp = VelopackApp.Build()
            .OnFirstRun(_ =>
            {
                Environment.SetEnvironmentVariable("WIREBOUND_FIRST_RUN", "1");
            })
            .OnRestarted(v =>
            {
                // Flag for showing What's New dialog after update restart
                Environment.SetEnvironmentVariable("WIREBOUND_UPDATED_TO", v?.ToString());
            });

        // OnBeforeUninstallFastCallback is Windows-only (fast-exit hook, never invoked on
        // other platforms) — guard it so the CA1416 platform-compatibility analyzer is satisfied.
        if (OperatingSystem.IsWindows())
        {
            ConfigureWindowsVelopackHooks(velopackApp);
        }

        velopackApp.Run();

        // Apply process mitigation policies BEFORE any plugin or native DLL
        // load that could be hijacked by an extension-point hook. This is
        // the only defense that meaningfully blocks in-process injection,
        // which is the realistic bypass of the IPC identity check.
        ProcessMitigations.ApplyEarly();

        // Release validation executes this from the published output. Constructing
        // the first chart catches binary incompatibilities between Avalonia and
        // LiveCharts without opening a window or touching the user's data.
        if (Array.Exists(args, arg => string.Equals(arg, StartupCheckArgument, StringComparison.OrdinalIgnoreCase)))
        {
            Environment.ExitCode = RunStartupCheck();
            return;
        }

        // Single-instance enforcement — exit immediately if another instance is running
        using var mutex = new Mutex(true, MutexName, out var createdNew);
        if (!createdNew)
        {
            Console.Error.WriteLine("WireBound is already running.");
            return;
        }

        // Configure Serilog early
        AppDataPaths.MigrateLegacyPersistentData();
        var logPath = AppDataPaths.GetPath("logs", "wirebound-.log");

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
            ReportFatalStartupError(ex, logPath);
            Environment.ExitCode = 1;
        }
        finally
        {
            Log.CloseAndFlush();
        }
    }

    private static int RunStartupCheck()
    {
        try
        {
            _ = new CartesianChart();
            Console.Out.WriteLine("WireBound startup compatibility check passed.");
            return 0;
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine($"WireBound startup compatibility check failed: {ex}");
            return 1;
        }
    }

    private static void ReportFatalStartupError(Exception exception, string logPath)
    {
        var logDirectory = Path.GetDirectoryName(logPath) ?? logPath;
        var message = $"WireBound couldn't start.\n\n{exception.GetType().Name}: {exception.Message}\n\n" +
                      $"Details were written to the logs in:\n{logDirectory}";

        Console.Error.WriteLine(message);

        if (!OperatingSystem.IsWindows())
        {
            return;
        }

        try
        {
            ShowWindowsErrorMessage(message);
        }
        catch
        {
            // The log and stderr still contain the startup error if the native
            // message box cannot be displayed (for example in a headless session).
        }
    }

    [SupportedOSPlatform("windows")]
    private static void ShowWindowsErrorMessage(string message) =>
        _ = MessageBox(IntPtr.Zero, message, "WireBound couldn't start", ErrorMessageBox);

    [DllImport("user32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern int MessageBox(IntPtr windowHandle, string text, string caption, uint type);

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
    private static void ConfigureWindowsVelopackHooks(VelopackApp velopackApp)
    {
        velopackApp.OnBeforeUpdateFastCallback(_ => StopWindowsElevationHelper());
        velopackApp.OnBeforeUninstallFastCallback(_ => CleanupWindowsStartupArtifacts());
    }

    [SupportedOSPlatform("windows")]
    private static void CleanupWindowsStartupArtifacts()
    {
        var startupService = new WindowsStartupService();

        StopWindowsElevationHelper(startupService);

        // Each call is bounded well under Velopack's 30-second OnBeforeUninstallFastCallback
        // hard limit (after which it force-exits the process) — a slow/pending UAC prompt on
        // the elevated Task Scheduler removal must not consume the entire uninstall budget.
        RunWithTimeout(() => startupService.SetStartupEnabledAsync(false), TimeSpan.FromSeconds(5));
        RunWithTimeout(() => startupService.SetHelperStartupEnabledAsync(false), TimeSpan.FromSeconds(20));
    }

    /// <summary>
    /// Stops the scheduled elevated helper before Velopack replaces the current
    /// application directory. Deleting a scheduled task alone does not reliably
    /// release a currently running elevated process's file handles.
    /// </summary>
    [SupportedOSPlatform("windows")]
    private static void StopWindowsElevationHelper() => StopWindowsElevationHelper(new WindowsStartupService());

    [SupportedOSPlatform("windows")]
    private static void StopWindowsElevationHelper(WindowsStartupService startupService) =>
        RunWithTimeout(startupService.StopHelperStartupTaskAsync, TimeSpan.FromSeconds(5));

    private static void RunWithTimeout(Func<Task<bool>> action, TimeSpan timeout)
    {
        try
        {
            Task.Run(action).Wait(timeout);
        }
        catch
        {
            // Best-effort cleanup — never block uninstall.
        }
    }
}
