namespace WireBound.Platform.Abstract.Services;

/// <summary>
/// Service for checking and requesting elevated (administrator/root) privileges.
/// </summary>
/// <remarks>
/// <para>
/// <b>Security Model:</b>
/// </para>
/// <para>
/// The current implementation uses a "full app elevation" pattern where the entire
/// application is restarted with elevated privileges. While this works, it is not
/// the ideal security pattern because the entire UI runs with elevated privileges.
/// </para>
/// <para>
/// <b>Future Work:</b> The preferred approach is to use a separate helper process
/// that runs with elevated privileges and communicates with the main UI process
/// via IPC (named pipes on Windows, Unix domain sockets on Linux). This isolates
/// privileged operations from the UI. See docs/DESIGN_PER_ADDRESS_TRACKING.md for
/// the full helper process architecture.
/// </para>
/// <para>
/// <b>Security Measures (Current Implementation):</b>
/// <list type="bullet">
/// <item>Validates that the process path is a valid, non-empty path</item>
/// <item>Logs all elevation attempts for audit purposes</item>
/// <item>Returns success/failure instead of auto-exiting, allowing UI to handle confirmation</item>
/// <item>Caller should confirm with user before calling TryElevateAsync</item>
/// </list>
/// </para>
/// </remarks>
public interface IElevationService
{
    /// <summary>
    /// Whether the current process is running with elevated privileges.
    /// On Windows, this checks for administrator token.
    /// On Linux, this checks if running as root (uid 0).
    /// </summary>
    bool IsElevated { get; }

    /// <summary>
    /// Whether elevation is required for advanced features but not currently available.
    /// This is true when <see cref="IsElevated"/> is false and the platform supports elevation.
    /// </summary>
    bool RequiresElevation { get; }

    /// <summary>
    /// Whether this platform supports elevation requests.
    /// Returns false for platforms where elevation is not implemented.
    /// </summary>
    bool IsElevationSupported { get; }

    /// <summary>
    /// Attempts to restart the application with elevated privileges.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <b>IMPORTANT:</b> The caller should display a confirmation dialog BEFORE calling
    /// this method. This method does not provide its own confirmation UI.
    /// </para>
    /// <para>
    /// On Windows, this triggers a UAC prompt. On Linux, this would use pkexec or similar.
    /// </para>
    /// <para>
    /// If successful on Windows, the caller should call <see cref="ExitAfterElevation"/>
    /// to cleanly exit the current process.
    /// </para>
    /// </remarks>
    /// <returns>
    /// A result indicating success, failure, or cancellation.
    /// Success means a new elevated process was started.
    /// </returns>
    Task<ElevationResult> TryElevateAsync();

    /// <summary>
    /// Cleanly exits the current process after successful elevation.
    /// Only call this after <see cref="TryElevateAsync"/> returns <see cref="ElevationStatus.Success"/>.
    /// </summary>
    /// <remarks>
    /// This is separated from TryElevateAsync to give the caller control over
    /// cleanup and timing of the exit.
    /// </remarks>
    void ExitAfterElevation();

    /// <summary>
    /// Checks if elevation is required for a specific feature.
    /// </summary>
    /// <param name="feature">The feature requiring elevation.</param>
    /// <returns>True if elevation is needed for the specified feature.</returns>
    bool RequiresElevationFor(ElevatedFeature feature);
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
