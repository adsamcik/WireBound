namespace WireBound.Core.Services;

/// <summary>
/// Service for checking, downloading, and applying application updates.
/// </summary>
public interface IUpdateService
{
    /// <summary>
    /// Checks for updates eligible after the 24-hour release waiting period. Returns null if none are ready.
    /// </summary>
    Task<UpdateCheckResult?> CheckForUpdateAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Downloads the specified update, reporting progress as a percentage (0-100).
    /// </summary>
    Task DownloadUpdateAsync(UpdateCheckResult update, Action<int>? progress = null, CancellationToken cancellationToken = default);

    /// <summary>
    /// Applies a previously downloaded update and restarts the application.
    /// </summary>
    Task ApplyUpdateAndRestartAsync(UpdateCheckResult update, CancellationToken cancellationToken = default);

    /// <summary>
    /// Gets whether in-app update installation is supported on the current platform.
    /// </summary>
    bool IsUpdateSupported { get; }

    /// <summary>
    /// Gets the currently installed application version.
    /// </summary>
    string CurrentVersion { get; }

    /// <summary>
    /// Gets a downloaded update verified against an eligible release during this session.
    /// Installation rechecks release eligibility and the package checksum.
    /// </summary>
    UpdateCheckResult? PreparedUpdate { get; }
}

/// <summary>
/// Framework-agnostic update check result. NativeUpdateInfo holds the Velopack UpdateInfo
/// object (opaque to Core layer).
/// </summary>
public record UpdateCheckResult(
    string Version,
    string? ReleaseNotesUrl,
    DateTimeOffset? PublishedAt,
    object? NativeUpdateInfo,
    bool CanInstallInApp = false);
