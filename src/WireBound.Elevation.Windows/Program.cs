using System.Runtime.Versioning;
using Serilog;
using WireBound.Elevation.Windows;

[assembly: SupportedOSPlatform("windows")]

Log.Logger = new LoggerConfiguration()
    .MinimumLevel.Information()
    .WriteTo.File(
        Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "WireBound", "elevation.log"),
        rollingInterval: RollingInterval.Day,
        retainedFileCountLimit: 3)
    .CreateLogger();

try
{
    Log.Information("WireBound Elevation starting (PID: {Pid})", Environment.ProcessId);

    // Parse the required --caller-sid argument
    var callerSid = CliParser.ParseCallerSid(args);
    if (callerSid is null)
    {
        Log.Fatal("Missing required argument --caller-sid <SID>. " +
                  "Usage: WireBound.Elevation --caller-sid <SID>");
        Environment.ExitCode = 1;
        return;
    }

    // Helper is built as WinExe so Windows does not allocate a console when the
    // process is launched via UAC. That makes Console.CancelKeyPress (Ctrl+C)
    // moot; instead rely on AppDomain.ProcessExit and the ElevationServer's own
    // IPC-disconnect detection to trigger graceful shutdown.
    using var cts = new CancellationTokenSource();
    AppDomain.CurrentDomain.ProcessExit += (_, _) =>
    {
        try { cts.Cancel(); }
        catch (ObjectDisposedException) { }
    };

    using var server = new ElevationServer(callerSid);
    await server.RunAsync(cts.Token);
}
catch (ArgumentException ex)
{
    Log.Fatal(ex, "Invalid caller SID argument");
    Environment.ExitCode = 1;
}
catch (OperationCanceledException)
{
    Log.Information("Elevation shutdown requested");
}
catch (Exception ex)
{
    Log.Fatal(ex, "Elevation process crashed");
    Environment.ExitCode = 1;
}
finally
{
    Log.Information("WireBound Elevation exiting");
    Log.CloseAndFlush();
}
