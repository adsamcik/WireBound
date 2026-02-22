using WireBound.Core.Services;

namespace WireBound.Tests.Fixtures;

/// <summary>
/// Test implementation of <see cref="IDispatcher"/> that executes actions synchronously
/// on the calling thread. Eliminates the need for Thread.Sleep in ViewModel tests.
/// </summary>
public sealed class SynchronousDispatcher : IUiDispatcher
{
    public void Post(Action action) => action();

    public void Post(Action action, UiDispatcherPriority priority) => action();

    public Task InvokeAsync(Action action)
    {
        action();
        return Task.CompletedTask;
    }
}
