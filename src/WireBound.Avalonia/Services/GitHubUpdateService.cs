using System.Net.Http;
using System.Net.Http.Json;
using System.Reflection;
using System.Text.Json;
using System.Text.Json.Serialization;
using Serilog;
using WireBound.Core.Services;

namespace WireBound.Avalonia.Services;

/// <summary>
/// Legacy update service using GitHub API only (no in-app download/apply).
/// Kept as fallback; VelopackUpdateService is the primary implementation.
/// </summary>
public partial class GitHubUpdateService : IUpdateService
{
    private const string GitHubApiUrl = "https://api.github.com/repos/adsamcik/WireBound/releases/latest";
    private static readonly HttpClient HttpClient = new()
    {
        DefaultRequestHeaders =
        {
            { "User-Agent", "WireBound-UpdateChecker" },
            { "Accept", "application/vnd.github.v3+json" }
        },
        Timeout = TimeSpan.FromSeconds(10)
    };

    public string CurrentVersion => Assembly.GetEntryAssembly()?.GetName().Version?.ToString(3) ?? "0.0.0";

    public bool IsUpdateSupported => false;

    public async Task<UpdateCheckResult?> CheckForUpdateAsync(CancellationToken cancellationToken = default)
    {
        try
        {
            var response = await HttpClient.GetAsync(GitHubApiUrl, cancellationToken);
            response.EnsureSuccessStatusCode();
            var json = await response.Content.ReadAsStringAsync(cancellationToken);
            var release = JsonSerializer.Deserialize(json, GitHubUpdateJsonContext.Default.GitHubRelease);
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
                null);
        }
        catch (Exception ex)
        {
            Log.Warning(ex, "Failed to check for updates");
            return null;
        }
    }

    public Task DownloadUpdateAsync(UpdateCheckResult update, Action<int>? progress = null, CancellationToken cancellationToken = default)
        => throw new NotSupportedException("In-app download is not supported by GitHubUpdateService. Use VelopackUpdateService.");

    public void ApplyUpdateAndRestart(UpdateCheckResult update)
        => throw new NotSupportedException("In-app update is not supported by GitHubUpdateService. Use VelopackUpdateService.");

    private record GitHubRelease(
        [property: JsonPropertyName("tag_name")] string TagName,
        [property: JsonPropertyName("html_url")] string HtmlUrl,
        [property: JsonPropertyName("published_at")] DateTimeOffset PublishedAt);

    [JsonSerializable(typeof(GitHubRelease))]
    private partial class GitHubUpdateJsonContext : JsonSerializerContext;
}
