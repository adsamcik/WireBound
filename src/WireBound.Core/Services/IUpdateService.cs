namespace WireBound.Core.Services;

/// <summary>
/// Service for checking, downloading, and applying application updates.
/// </summary>
public interface IUpdateService
{
    /// <summary>
    /// Checks for available updates. Returns null if no update is available.
    /// </summary>
    Task<UpdateCheckResult?> CheckForUpdateAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Downloads the specified update, reporting progress as a percentage (0-100).
    /// </summary>
    Task DownloadUpdateAsync(UpdateCheckResult update, Action<int>? progress = null, CancellationToken cancellationToken = default);

    /// <summary>
    /// Applies a previously downloaded update and restarts the application.
    /// </summary>
    void ApplyUpdateAndRestart(UpdateCheckResult update);

    /// <summary>
    /// Gets whether in-app update installation is supported on the current platform.
    /// </summary>
    bool IsUpdateSupported { get; }

    /// <summary>
    /// Gets the currently installed application version.
    /// </summary>
    string CurrentVersion { get; }
}

/// <summary>
/// Framework-agnostic update check result. NativeUpdateInfo holds the Velopack UpdateInfo
/// object (opaque to Core layer).
/// </summary>
public record UpdateCheckResult(
    string Version,
    string? ReleaseNotesUrl,
    DateTimeOffset? PublishedAt,
    object? NativeUpdateInfo);