using Avalonia.Threading;
using WireBound.Core.Services;

namespace WireBound.Avalonia.Services;

/// <summary>
/// Avalonia implementation of <see cref="IDispatcher"/> that delegates to
/// <see cref="Dispatcher.UIThread"/>.
/// </summary>
public sealed class AvaloniaDispatcher : IUiDispatcher
{
    public void Post(Action action)
        => Dispatcher.UIThread.Post(action);

    public void Post(Action action, UiDispatcherPriority priority)
        => Dispatcher.UIThread.Post(action, priority switch
        {
            UiDispatcherPriority.Background => DispatcherPriority.Background,
            _ => DispatcherPriority.Normal
        });

    public async Task InvokeAsync(Action action)
        => await Dispatcher.UIThread.InvokeAsync(action);
}
