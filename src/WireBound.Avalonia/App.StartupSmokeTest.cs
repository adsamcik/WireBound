using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Threading;
using Avalonia.VisualTree;
using Serilog;
using WireBound.Avalonia.ViewModels;
using WireBound.Avalonia.Views;
using WireBound.Core;

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

            await CheckMemoryNavigationAsync(mainWindow);
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

    private static async Task CheckMemoryNavigationAsync(MainWindow mainWindow)
    {
        await Dispatcher.UIThread.InvokeAsync(() =>
        {
            var openMemory = mainWindow.GetVisualDescendants().OfType<Button>()
                .Single(b => AutomationProperties.GetName(b) == "Open Memory");
            if (openMemory.Command?.CanExecute(openMemory.CommandParameter) != true)
                throw new InvalidOperationException("The Memory navigation action is unavailable.");
            openMemory.Command.Execute(openMemory.CommandParameter);
        });

        MemoryView? memoryView = null;
        for (var attempt = 0; attempt < 200; attempt++)
        {
            var ready = await Dispatcher.UIThread.InvokeAsync(() =>
            {
                memoryView = (mainWindow.DataContext as MainViewModel)?.CurrentView as MemoryView;
                return memoryView is
                {
                    Bounds.Width: > 0, Bounds.Height: > 0,
                    DataContext: MemoryViewModel { IsLoading: false, HasMemorySegments: true }
                };
            }, DispatcherPriority.Background);
            if (ready) break;
            await Task.Delay(100);
        }

        await Dispatcher.UIThread.InvokeAsync(() =>
        {
            if (memoryView?.DataContext is not MemoryViewModel { IsLoading: false, HasMemorySegments: true } memory)
                throw new InvalidOperationException("The Memory route did not produce a live RAM sample.");
            var bar = memoryView.FindControl<Grid>("MemoryCapacityBar");
            var list = memoryView.FindControl<ListBox>("ConsumerList");
            if (bar is not { Bounds.Width: > 0, Bounds.Height: >= 40 } || list is not { Bounds.Height: > 0 }
                || bar.Children.Count != memory.MemorySegments.Count || memory.Consumers.Count == 0)
                throw new InvalidOperationException("The live Memory bar and consumers were not rendered.");

            var slices = bar.Children.OfType<Button>().ToArray();
            var slice = slices.FirstOrDefault(b => b.DataContext is MemorySegment { ConsumerId: { } id }
                && id.StartsWith("app:", StringComparison.Ordinal)) ?? slices.First();
            var segment = (MemorySegment)slice.DataContext!;
            if (slice.Command?.CanExecute(slice.CommandParameter) != true)
                throw new InvalidOperationException("The live Memory slice is not connected to navigation.");
            slice.Command.Execute(slice.CommandParameter);
            if (memory.SelectedConsumer?.Id != segment.ConsumerId || !slice.Classes.Contains("selected"))
                throw new InvalidOperationException("The live Memory slice did not reveal its consumer.");

            var main = (MainViewModel)mainWindow.DataContext!;
            main.NavigateToCommand.Execute(Routes.Overview);
            main.NavigateToCommand.Execute(Routes.Memory);
            if (!ReferenceEquals(main.CurrentView, memoryView) || (main.CurrentView as MemoryView)?.DataContext != memory)
                throw new InvalidOperationException("Returning to Memory did not retain the connected page.");

            Log.Information("Startup smoke test reached the live Memory page with {Consumers} consumers, {Segments} RAM slices and working slice navigation",
                memory.Consumers.Count, memory.MemorySegments.Count);
        }, DispatcherPriority.Background);
    }
}
