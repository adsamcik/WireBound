namespace WireBound.Core.Services;

/// <summary>
/// Service for checking and requesting elevated (administrator/root) privileges.
/// </summary>
public interface IElevationService
{
    /// <summary>
    /// Whether the current process is running with elevated privileges.
    /// </summary>
    bool IsElevated { get; }

    /// <summary>
    /// Attempts to restart the application with elevated privileges.
    /// On Windows, this triggers a UAC prompt. On Linux, this may use pkexec or sudo.
    /// </summary>
    /// <returns>True if elevation was requested (app will restart), false if cancelled or failed.</returns>
    Task<bool> RequestElevationAsync();

    /// <summary>
    /// Checks if elevation is required for a specific feature.
    /// </summary>
    /// <param name="feature">The feature requiring elevation.</param>
    /// <returns>True if elevation is needed.</returns>
    bool RequiresElevationFor(ElevatedFeature feature);
}

/// <summary>
/// Features that may require elevated privileges.
/// </summary>
public enum ElevatedFeature
{
    /// <summary>
    /// Per-process network monitoring using platform-specific APIs.
    /// </summary>
    PerProcessNetworkMonitoring,

    /// <summary>
    /// Raw socket capture for advanced monitoring.
    /// </summary>
    RawSocketCapture
}
