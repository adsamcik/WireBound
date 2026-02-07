namespace WireBound.Core.Services;

public interface IUpdateService
{
    Task<UpdateInfo?> CheckForUpdateAsync(CancellationToken cancellationToken = default);
    string CurrentVersion { get; }
}

public record UpdateInfo(string Version, string DownloadUrl, string ReleaseNotesUrl, DateTimeOffset PublishedAt);
