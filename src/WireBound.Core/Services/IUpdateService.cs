namespace WireBound.Core.Services;

public interface IUpdateService
{
    Task<UpdateCheckResult?> CheckForUpdateAsync(CancellationToken cancellationToken = default);
    Task DownloadUpdateAsync(UpdateCheckResult update, Action<int>? progress = null, CancellationToken cancellationToken = default);
    void ApplyUpdateAndRestart(UpdateCheckResult update);
    bool IsUpdateSupported { get; }
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