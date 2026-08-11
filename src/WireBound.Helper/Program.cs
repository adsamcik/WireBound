using Serilog;
using WireBound.Helper;
using WireBound.Platform.Abstract.Helpers;

// Configure logging to a dedicated helper log file
Log.Logger = new LoggerConfiguration()
    .MinimumLevel.Information()
    .WriteTo.File(
        AppDataPaths.GetPath("helper.log"),
        rollingInterval: RollingInterval.Day,
        retainedFileCountLimit: 3)
    .CreateLogger();

try
{
    Log.Information("WireBound Helper starting (PID: {Pid})", Environment.ProcessId);

    using var cts = new CancellationTokenSource();
    Console.CancelKeyPress += (_, e) =>
    {
        e.Cancel = true;
        cts.Cancel();
    };

    var server = new HelperServer();
    await server.RunAsync(cts.Token);
}
catch (OperationCanceledException)
{
    Log.Information("Helper shutdown requested");
}
catch (Exception ex)
{
    Log.Fatal(ex, "Helper crashed");
    Environment.ExitCode = 1;
}
finally
{
    Log.Information("WireBound Helper exiting");
    Log.CloseAndFlush();
}
