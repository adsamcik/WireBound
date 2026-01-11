namespace WireBound.Services;

/// <summary>
/// Service for checking and requesting elevated (administrator) privileges.
/// </summary>
public interface IElevationService
{
    /// <summary>
    /// Whether the current process is running with administrator privileges.
    /// </summary>
    bool IsElevated { get; }

    /// <summary>
    /// Attempts to restart the application with elevated privileges.
    /// This will trigger a UAC prompt and restart the app if approved.
    /// </summary>
    /// <returns>True if elevation was requested (app will restart), false if cancelled or failed.</returns>
    Task<bool> RequestElevationAsync();

    /// <summary>
    /// Checks if elevation is required for a specific feature.
    /// </summary>
    /// <param name="featureName">The name of the feature requiring elevation.</param>
    /// <returns>True if elevation is needed.</returns>
    bool RequiresElevationFor(ElevatedFeature feature);
}

/// <summary>
/// Features that may require elevated privileges.
/// </summary>
public enum ElevatedFeature
{
    /// <summary>
    /// Per-process network monitoring using ETW or IP Helper API.
    /// </summary>
    PerProcessNetworkMonitoring,

    /// <summary>
    /// Raw socket capture for advanced monitoring.
    /// </summary>
    RawSocketCapture
}
