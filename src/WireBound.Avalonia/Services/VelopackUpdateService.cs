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
public sealed partial class VelopackUpdateService : IUpdateService
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
    public string CurrentVersion => _updateManager.CurrentVersion?.ToString()
        ?? Assembly.GetEntryAssembly()?.GetName().Version?.ToString(3)
        ?? "0.0.0";

    /// <inheritdoc />
    public UpdateCheckResult? PreparedUpdate
    {
        get
        {
            var prepared = _updateManager.UpdatePendingRestart;
            return prepared is null
                ? null
                : new UpdateCheckResult(
                    prepared.Version.ToString(),
                    GetReleaseUrl(prepared.Version.ToString()),
                    null,
                    prepared,
                    CanInstallInApp: true);
        }
    }

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
                GetReleaseUrl(info.TargetFullRelease.Version.ToString()),
                null,
                info,
                CanInstallInApp: true);
        }
        catch (Exception ex)
        {
            Log.Warning(ex, "Velopack update check failed, falling back to GitHub API");
            try
            {
                return await CheckGitHubApiAsync(cancellationToken);
            }
            catch (Exception fallbackException)
            {
                throw new InvalidOperationException(
                    "WireBound could not reach its update service. Check your connection and try again.",
                    new AggregateException(ex, fallbackException));
            }
        }
    }

    /// <inheritdoc />
    public async Task DownloadUpdateAsync(UpdateCheckResult update, Action<int>? progress = null, CancellationToken cancellationToken = default)
    {
        if (!IsUpdateSupported)
            throw new InvalidOperationException("In-app updates are not supported in portable mode.");

        if (!update.CanInstallInApp)
            throw new InvalidOperationException("This update must be installed from the release download page.");

        if (update.NativeUpdateInfo is not UpdateInfo info)
            throw new ArgumentException("Invalid update info — expected Velopack UpdateInfo.", nameof(update));

        await _updateManager.DownloadUpdatesAsync(info, progress, cancelToken: cancellationToken);
    }

    /// <inheritdoc />
    public void ApplyUpdateAndRestart(UpdateCheckResult update)
    {
        if (!IsUpdateSupported)
            throw new InvalidOperationException("In-app updates are not supported in portable mode.");

        if (!update.CanInstallInApp)
            throw new InvalidOperationException("This update must be installed from the release download page.");

        switch (update.NativeUpdateInfo)
        {
            case UpdateInfo info:
                _updateManager.ApplyUpdatesAndRestart(info);
                break;
            case VelopackAsset prepared:
                _updateManager.ApplyUpdatesAndRestart(prepared);
                break;
            default:
                throw new ArgumentException("Invalid update information.", nameof(update));
        }
    }

    /// <summary>
    /// Fallback for portable mode — checks GitHub API for latest release.
    /// </summary>
    private async Task<UpdateCheckResult?> CheckGitHubApiAsync(CancellationToken cancellationToken)
    {
        using var response = await HttpClient.GetAsync(GitHubApiUrl, cancellationToken);
        response.EnsureSuccessStatusCode();
        var json = await response.Content.ReadAsStringAsync(cancellationToken);
        var release = JsonSerializer.Deserialize(json, VelopackJsonContext.Default.GitHubRelease)
            ?? throw new InvalidDataException("GitHub returned an invalid release response.");

        var latestVersion = release.TagName.TrimStart('v');
        if (!Version.TryParse(latestVersion, out var latest) ||
            !Version.TryParse(CurrentVersion, out var current))
        {
            throw new InvalidDataException("The release version could not be parsed.");
        }

        if (latest <= current) return null;

        return new UpdateCheckResult(
            latestVersion,
            release.HtmlUrl,
            release.PublishedAt,
            null,
            CanInstallInApp: false);
    }

    private static string GetReleaseUrl(string version) =>
        $"https://github.com/{Owner}/{Repo}/releases/tag/v{version}";

    private record GitHubRelease(
        [property: JsonPropertyName("tag_name")] string TagName,
        [property: JsonPropertyName("html_url")] string HtmlUrl,
        [property: JsonPropertyName("published_at")] DateTimeOffset PublishedAt);

    [JsonSerializable(typeof(GitHubRelease))]
    private partial class VelopackJsonContext : JsonSerializerContext;
}
