namespace WireBound.Platform.Abstract.Services;

/// <summary>
/// Service for checking elevation status and managing elevated helper process.
/// </summary>
/// <remarks>
/// <para>
/// <b>Security Model:</b>
/// </para>
/// <para>
/// This service uses a "minimal helper process" pattern where only a small, tightly-scoped
/// helper process runs with elevated privileges. The main UI application NEVER runs elevated.
/// This isolates privileged operations from the UI, following the principle of least privilege.
/// </para>
/// <para>
/// <b>Helper Process Architecture:</b>
/// <list type="bullet">
/// <item>Helper is a separate, minimal executable with limited functionality</item>
/// <item>Helper only exposes specific, well-defined operations (ETW/eBPF data collection)</item>
/// <item>Communication via IPC (named pipes on Windows, Unix sockets on Linux)</item>
/// <item>Helper validates client identity before accepting connections</item>
/// <item>Session-based with automatic timeout (max 8 hours)</item>
/// <item>Rate-limited to prevent abuse</item>
/// </list>
/// </para>
/// <para>
/// <b>Security Measures:</b>
/// <list type="bullet">
/// <item>Main application NEVER runs with elevated privileges</item>
/// <item>Helper process is minimal and only provides data collection APIs</item>
/// <item>Helper validates that client is the legitimate WireBound application</item>
/// <item>All IPC messages are authenticated with HMAC signatures</item>
/// <item>Helper has no UI, no network access, no file system write access</item>
/// <item>All elevation attempts are logged for audit purposes</item>
/// </list>
/// </para>
/// <para>
/// See docs/DESIGN_PER_ADDRESS_TRACKING.md for the full helper process architecture.
/// </para>
/// </remarks>
public interface IElevationService
{
    /// <summary>
    /// Whether the elevated helper process is currently running and connected.
    /// The main UI application should NEVER be elevated itself.
    /// </summary>
    bool IsHelperConnected { get; }

    /// <summary>
    /// Whether the current process is running with elevated privileges.
    /// On Windows, this checks for administrator token.
    /// On Linux, this checks if running as root (uid 0).
    /// </summary>
    /// <remarks>
    /// <b>IMPORTANT:</b> If this returns true for the main UI process, it indicates
    /// a security concern - the UI should not be running elevated. The proper pattern
    /// is to use the helper process for elevated operations.
    /// </remarks>
    bool IsElevated { get; }

    /// <summary>
    /// Whether elevation is required for advanced features but helper is not connected.
    /// </summary>
    bool RequiresElevation { get; }

    /// <summary>
    /// Whether this platform supports the elevated helper process.
    /// Returns false for platforms where the helper is not implemented.
    /// </summary>
    bool IsElevationSupported { get; }

    /// <summary>
    /// Attempts to start the minimal elevated helper process.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <b>IMPORTANT:</b> This does NOT elevate the current application. It starts
    /// a separate, minimal helper process that runs with elevated privileges.
    /// </para>
    /// <para>
    /// On Windows, this triggers a UAC prompt for the helper executable only.
    /// On Linux, this uses pkexec or polkit for the helper.
    /// </para>
    /// <para>
    /// The helper process:
    /// <list type="bullet">
    /// <item>Is a separate, minimal executable</item>
    /// <item>Only provides specific data collection APIs</item>
    /// <item>Validates the calling application before accepting connections</item>
    /// <item>Has strict rate limiting and session management</item>
    /// </list>
    /// </para>
    /// </remarks>
    /// <param name="cancellationToken">Cancellation token</param>
    /// <returns>
    /// A result indicating success, failure, or cancellation.
    /// Success means the helper process was started and connection established.
    /// </returns>
    Task<ElevationResult> StartHelperAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Stops the elevated helper process.
    /// </summary>
    /// <remarks>
    /// This gracefully shuts down the helper process. It should be called
    /// when elevated features are no longer needed, or when the application exits.
    /// </remarks>
    Task StopHelperAsync();

    /// <summary>
    /// Gets the connection to the elevated helper process, if connected.
    /// </summary>
    /// <returns>The helper connection, or null if not connected.</returns>
    IHelperConnection? GetHelperConnection();

    /// <summary>
    /// Checks if elevation is required for a specific feature.
    /// </summary>
    /// <param name="feature">The feature requiring elevation.</param>
    /// <returns>True if elevation is needed for the specified feature.</returns>
    bool RequiresElevationFor(ElevatedFeature feature);

    /// <summary>
    /// Event raised when the helper connection state changes.
    /// </summary>
    event EventHandler<HelperConnectionStateChangedEventArgs>? HelperConnectionStateChanged;
}

/// <summary>
/// Event args for helper connection state changes.
/// </summary>
public class HelperConnectionStateChangedEventArgs : EventArgs
{
    /// <summary>
    /// Whether the helper is now connected.
    /// </summary>
    public bool IsConnected { get; }

    /// <summary>
    /// Reason for the state change.
    /// </summary>
    public string Reason { get; }

    public HelperConnectionStateChangedEventArgs(bool isConnected, string reason)
    {
        IsConnected = isConnected;
        Reason = reason;
    }
}

/// <summary>
/// Result of an elevation attempt.
/// </summary>
/// <param name="Status">The status of the elevation attempt.</param>
/// <param name="ErrorMessage">Optional error message if elevation failed.</param>
public readonly record struct ElevationResult(ElevationStatus Status, string? ErrorMessage = null)
{
    /// <summary>
    /// Creates a successful result.
    /// </summary>
    public static ElevationResult Success() => new(ElevationStatus.Success);

    /// <summary>
    /// Creates a cancelled result (user cancelled UAC or similar).
    /// </summary>
    public static ElevationResult Cancelled() => new(ElevationStatus.Cancelled);

    /// <summary>
    /// Creates a failed result with an error message.
    /// </summary>
    public static ElevationResult Failed(string message) => new(ElevationStatus.Failed, message);

    /// <summary>
    /// Creates a not-supported result (platform doesn't support elevation).
    /// </summary>
    public static ElevationResult NotSupported() => new(ElevationStatus.NotSupported, "Elevation is not supported on this platform");

    /// <summary>
    /// Whether the elevation was successful.
    /// </summary>
    public bool IsSuccess => Status == ElevationStatus.Success;
}

/// <summary>
/// Status of an elevation attempt.
/// </summary>
public enum ElevationStatus
{
    /// <summary>
    /// Elevation succeeded - a new elevated process was started.
    /// </summary>
    Success,

    /// <summary>
    /// User cancelled the elevation prompt (e.g., dismissed UAC dialog).
    /// </summary>
    Cancelled,

    /// <summary>
    /// Elevation failed due to an error.
    /// </summary>
    Failed,

    /// <summary>
    /// Elevation is not supported on this platform.
    /// </summary>
    NotSupported
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
