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

    using var cts = new CancellationTokenSource();
    Console.CancelKeyPress += (_, e) =>
    {
        e.Cancel = true;
        cts.Cancel();
    };

    AppDomain.CurrentDomain.ProcessExit += (_, _) => cts.Cancel();

    using var server = new ElevationServer();
    await server.RunAsync(cts.Token);
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
