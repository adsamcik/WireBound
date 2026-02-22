namespace WireBound.Core.Services;

/// <summary>
/// Abstraction over the UI thread dispatcher.
/// Enables ViewModels to marshal work to the UI thread without depending on a specific UI framework.
/// Named IUiDispatcher to avoid conflict with Avalonia.Threading.IDispatcher.
/// </summary>
public interface IUiDispatcher
{
    /// <summary>
    /// Posts an action to the UI thread at normal priority without waiting for completion.
    /// </summary>
    void Post(Action action);

    /// <summary>
    /// Posts an action to the UI thread at the specified priority without waiting for completion.
    /// Use <see cref="UiDispatcherPriority.Background"/> for data updates that should yield to user input.
    /// </summary>
    void Post(Action action, UiDispatcherPriority priority);

    /// <summary>
    /// Invokes an action on the UI thread and returns a task that completes when the action finishes.
    /// </summary>
    Task InvokeAsync(Action action);
}
