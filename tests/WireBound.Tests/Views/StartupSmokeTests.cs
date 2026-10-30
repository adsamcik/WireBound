using System.Diagnostics;
using Microsoft.EntityFrameworkCore;
using WireBound.Core.Data;

namespace WireBound.Tests.Views;

public class StartupSmokeTests
{
    [Test]
    [Arguments(false)]
    [Arguments(true)]
    [Timeout(90_000)]
    public async Task DesktopStartup_RendersDashboardAndShutsDownWithoutErrors(bool minimizedFirstRun, CancellationToken cancellationToken)
    {
        var dataDirectory = Path.Combine(Path.GetTempPath(), $"wirebound-startup-{Guid.NewGuid():N}");
        Directory.CreateDirectory(dataDirectory);
        using var process = new Process();
        var started = false;
        try
        {
            if (minimizedFirstRun)
            {
                var options = new DbContextOptionsBuilder<WireBoundDbContext>()
                    .UseSqlite($"Data Source={Path.Combine(dataDirectory, "wirebound.db")};Pooling=False")
                    .Options;
                await using var context = new WireBoundDbContext(options);
                await context.Database.EnsureCreatedAsync(cancellationToken);
                var settings = await context.Settings.SingleAsync(cancellationToken);
                settings.StartMinimized = true;
                await context.SaveChangesAsync(cancellationToken);
            }

            // Launch the apphost with its own runtime settings and dependency graph.
            // A child process keeps Avalonia's singleton dispatcher out of other tests.
            var appAssembly = typeof(WireBound.Avalonia.App).Assembly.Location;
            var executable = Path.ChangeExtension(appAssembly, OperatingSystem.IsWindows() ? ".exe" : null);
            File.Exists(executable).Should().BeTrue("the apphost must be present in the build output");
            var startInfo = new ProcessStartInfo(executable)
            {
                UseShellExecute = false,
                CreateNoWindow = true,
                RedirectStandardOutput = true,
                RedirectStandardError = true
            };
            startInfo.ArgumentList.Add("--startup-smoke-test");
            startInfo.Environment["WIREBOUND_STARTUP_TEST_DATA_DIRECTORY"] = dataDirectory;
            startInfo.Environment["WIREBOUND_FIRST_RUN"] = minimizedFirstRun ? "1" : "0";
            startInfo.Environment.Remove("WIREBOUND_UPDATED_TO");
            process.StartInfo = startInfo;
            started = process.Start();
            started.Should().BeTrue();
            var standardOutput = process.StandardOutput.ReadToEndAsync();
            var standardError = process.StandardError.ReadToEndAsync();

            using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            timeout.CancelAfter(TimeSpan.FromSeconds(60));
            try
            {
                await process.WaitForExitAsync(timeout.Token);
            }
            catch (OperationCanceledException)
            {
                process.Kill(entireProcessTree: true);
                await process.WaitForExitAsync();
                throw new TimeoutException($"The desktop app did not finish startup and shutdown within 60 seconds.\n{ReadLogs(dataDirectory)}\n{await standardError}");
            }

            var logs = ReadLogs(dataDirectory);
            var diagnostics = $"stdout:\n{await standardOutput}\nstderr:\n{await standardError}\nlogs:\n{logs}";
            process.ExitCode.Should().Be(0, diagnostics);
            logs.Should().Contain("Database initialized successfully", diagnostics)
                .And.Contain("Application async initialization completed", diagnostics)
                .And.Contain("Startup smoke test reached a rendered dashboard with active monitoring", diagnostics)
                .And.Contain("Application shutdown complete", diagnostics)
                .And.NotContain("[ERR]", diagnostics)
                .And.NotContain("[FTL]", diagnostics);
            if (minimizedFirstRun)
                logs.Should().Contain("Showed first-install welcome dialog", diagnostics);
        }
        finally
        {
            if (started && !process.HasExited)
            {
                process.Kill(entireProcessTree: true);
                await process.WaitForExitAsync();
            }

            Directory.Delete(dataDirectory, recursive: true);
        }
    }

    private static string ReadLogs(string dataDirectory)
    {
        var logDirectory = Path.Combine(dataDirectory, "logs");
        return Directory.Exists(logDirectory)
            ? string.Join('\n', Directory.GetFiles(logDirectory, "*.log").Select(File.ReadAllText))
            : "No application logs were created.";
    }
}
