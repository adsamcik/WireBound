namespace WireBound.Core.Services;

/// <summary>
/// Priority levels for UI dispatcher operations.
/// Lower-priority operations yield to higher-priority ones in the dispatcher queue.
/// </summary>
public enum UiDispatcherPriority
{
    /// <summary>
    /// Background priority — processed after all Normal and higher operations.
    /// Use for data updates that should yield to user input and rendering.
    /// </summary>
    Background = 0,

    /// <summary>
    /// Normal priority — default for user-initiated actions and rendering.
    /// </summary>
    Normal = 1
}
