namespace WireBound.Core.Services;

/// <summary>
/// Interface for the network polling background service, allowing runtime configuration updates.
/// </summary>
public interface INetworkPollingBackgroundService
{
    /// <summary>
    /// Updates the polling interval at runtime without requiring app restart.
    /// </summary>
    /// <param name="milliseconds">The new polling interval in milliseconds.</param>
    void UpdatePollingInterval(int milliseconds);

    /// <summary>
    /// Updates the save interval at runtime without requiring app restart.
    /// </summary>
    /// <param name="seconds">The new save interval in seconds.</param>
    void UpdateSaveInterval(int seconds);

    /// <summary>
    /// Enables or disables adaptive polling, which dynamically adjusts the polling interval
    /// based on CPU usage to reduce system load under high pressure.
    /// </summary>
    /// <param name="enabled">Whether adaptive polling should be active.</param>
    /// <param name="baseIntervalMs">The base polling interval to use when CPU load is normal.</param>
    void SetAdaptivePolling(bool enabled, int baseIntervalMs);

    /// <summary>
    /// Gets the current effective polling interval in milliseconds.
    /// May differ from the configured interval when adaptive polling is active.
    /// </summary>
    int CurrentPollingIntervalMs { get; }
}
