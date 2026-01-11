namespace WireBound.Services;

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
}
