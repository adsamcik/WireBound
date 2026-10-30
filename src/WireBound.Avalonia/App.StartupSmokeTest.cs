using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Threading;
using Serilog;
using WireBound.Avalonia.ViewModels;
using WireBound.Avalonia.Views;

namespace WireBound.Avalonia;

public partial class App
{
    /// <summary>
    /// Runs inside the real desktop process, after its native window has opened.
    /// The parent test supplies isolated storage, enforces a timeout, and checks the logs.
    /// </summary>
    private static async Task CompleteStartupSmokeTestAsync(
        IClassicDesktopStyleApplicationLifetime desktop, MainWindow mainWindow, Task initialization)
    {
        try
        {
            await initialization;

            // Let native rendering, chart updates and several polling ticks execute.
            // A window constructor alone misses failures posted to the dispatcher.
            await Task.Delay(TimeSpan.FromSeconds(5));
            await Dispatcher.UIThread.InvokeAsync(() =>
            {
                if (!mainWindow.IsVisible || mainWindow.Bounds.Width <= 0 || mainWindow.Bounds.Height <= 0
                    || mainWindow.DataContext is not MainViewModel { IsMonitoringActive: true } viewModel
                    || viewModel.CurrentView is not OverviewView { Bounds.Width: > 0, Bounds.Height: > 0 })
                {
                    throw new InvalidOperationException("The startup window, dashboard or monitoring is not ready.");
                }

                Log.Information("Startup smoke test reached a rendered dashboard with active monitoring");
            }, DispatcherPriority.Background);
        }
        catch (Exception ex)
        {
            Log.Error(ex, "Startup smoke test failed");
            Environment.ExitCode = 1;
        }
        finally
        {
            // TryShutdown exercises the application's normal async cleanup handler.
            await Dispatcher.UIThread.InvokeAsync(() => desktop.TryShutdown());
        }
    }
}
