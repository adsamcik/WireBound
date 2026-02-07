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
        Log.Fatal("Missing required argument: --caller-sid <SID>");
        Console.Error.WriteLine("Usage: WireBound.Elevation --caller-sid <SID>");
        Environment.ExitCode = 1;
        return;
    }

    using var cts = new CancellationTokenSource();
    Console.CancelKeyPress += (_, e) =>
    {
        e.Cancel = true;
        cts.Cancel();
    };

    AppDomain.CurrentDomain.ProcessExit += (_, _) => cts.Cancel();

    using var server = new ElevationServer(callerSid);
    await server.RunAsync(cts.Token);
}
catch (ArgumentException ex)
{
    Log.Fatal(ex, "Invalid caller SID argument");
    Console.Error.WriteLine($"Error: {ex.Message}");
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
