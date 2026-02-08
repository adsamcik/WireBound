using System.Net.Http;
using System.Net.Http.Json;
using System.Reflection;
using System.Text.Json;
using System.Text.Json.Serialization;
using Serilog;
using Velopack;
using Velopack.Sources;
using WireBound.Core.Services;

namespace WireBound.Avalonia.Services;

/// <summary>
/// Update service using Velopack for installed mode, with GitHub API fallback for portable mode.
/// </summary>
public sealed class VelopackUpdateService : IUpdateService
{
    private const string Owner = "adsamcik";
    private const string Repo = "WireBound";
    private const string RepoUrl = $"https://github.com/{Owner}/{Repo}";
    private const string GitHubApiUrl = $"https://api.github.com/repos/{Owner}/{Repo}/releases/latest";

    private static readonly HttpClient HttpClient = new()
    {
        DefaultRequestHeaders =
        {
            { "User-Agent", "WireBound-UpdateChecker" },
            { "Accept", "application/vnd.github.v3+json" }
        },
        Timeout = TimeSpan.FromSeconds(10)
    };

    private readonly UpdateManager _updateManager;

    public VelopackUpdateService()
    {
        var source = new GithubSource(RepoUrl, accessToken: null, prerelease: false);
        _updateManager = new UpdateManager(source);
    }

    /// <inheritdoc />
    public bool IsUpdateSupported => _updateManager.IsInstalled;

    /// <inheritdoc />
    public string CurrentVersion =>
        Assembly.GetEntryAssembly()?.GetName().Version?.ToString(3) ?? "0.0.0";

    /// <inheritdoc />
    public async Task<UpdateCheckResult?> CheckForUpdateAsync(CancellationToken cancellationToken = default)
    {
        if (!IsUpdateSupported)
        {
            return await CheckGitHubApiAsync(cancellationToken);
        }

        try
        {
            var info = await _updateManager.CheckForUpdatesAsync();
            if (info is null) return null;

            return new UpdateCheckResult(
                info.TargetFullRelease.Version.ToString(),
                $"https://github.com/{Owner}/{Repo}/releases/tag/v{info.TargetFullRelease.Version}",
                null,
                info);
        }
        catch (Exception ex)
        {
            Log.Warning(ex, "Velopack update check failed, falling back to GitHub API");
            return await CheckGitHubApiAsync(cancellationToken);
        }
    }

    /// <inheritdoc />
    public async Task DownloadUpdateAsync(UpdateCheckResult update, Action<int>? progress = null, CancellationToken cancellationToken = default)
    {
        if (!IsUpdateSupported)
            throw new InvalidOperationException("In-app updates are not supported in portable mode.");

        if (update.NativeUpdateInfo is not UpdateInfo info)
            throw new ArgumentException("Invalid update info — expected Velopack UpdateInfo.", nameof(update));

        await _updateManager.DownloadUpdatesAsync(info, progress, cancelToken: cancellationToken);
    }

    /// <inheritdoc />
    public void ApplyUpdateAndRestart(UpdateCheckResult update)
    {
        if (!IsUpdateSupported)
            throw new InvalidOperationException("In-app updates are not supported in portable mode.");

        if (update.NativeUpdateInfo is not UpdateInfo info)
            throw new ArgumentException("Invalid update info — expected Velopack UpdateInfo.", nameof(update));

        _updateManager.ApplyUpdatesAndRestart(info);
    }

    /// <summary>
    /// Fallback for portable mode — checks GitHub API for latest release.
    /// </summary>
    private async Task<UpdateCheckResult?> CheckGitHubApiAsync(CancellationToken cancellationToken)
    {
        try
        {
            var response = await HttpClient.GetAsync(GitHubApiUrl, cancellationToken);
            response.EnsureSuccessStatusCode();
            var json = await response.Content.ReadAsStringAsync(cancellationToken);
            var release = JsonSerializer.Deserialize<GitHubRelease>(json);
            if (release is null) return null;

            var latestVersion = release.TagName.TrimStart('v');
            if (!Version.TryParse(latestVersion, out var latest) ||
                !Version.TryParse(CurrentVersion, out var current))
                return null;

            if (latest <= current) return null;

            return new UpdateCheckResult(
                latestVersion,
                release.HtmlUrl,
                release.PublishedAt,
                null); // No native info for portable mode
        }
        catch (Exception ex)
        {
            Log.Warning(ex, "Failed to check for updates via GitHub API");
            return null;
        }
    }

    private record GitHubRelease(
        [property: JsonPropertyName("tag_name")] string TagName,
        [property: JsonPropertyName("html_url")] string HtmlUrl,
        [property: JsonPropertyName("published_at")] DateTimeOffset PublishedAt);
}
