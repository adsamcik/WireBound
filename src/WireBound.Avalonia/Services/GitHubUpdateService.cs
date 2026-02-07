using System.Net.Http;
using System.Net.Http.Json;
using System.Reflection;
using System.Text.Json;
using System.Text.Json.Serialization;
using Serilog;
using WireBound.Core.Services;

namespace WireBound.Avalonia.Services;

public class GitHubUpdateService : IUpdateService
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

    public async Task<UpdateInfo?> CheckForUpdateAsync(CancellationToken cancellationToken = default)
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

            return new UpdateInfo(
                latestVersion,
                release.HtmlUrl,
                release.HtmlUrl,
                release.PublishedAt);
        }
        catch (Exception ex)
        {
            Log.Warning(ex, "Failed to check for updates");
            return null;
        }
    }

    private record GitHubRelease(
        [property: JsonPropertyName("tag_name")] string TagName,
        [property: JsonPropertyName("html_url")] string HtmlUrl,
        [property: JsonPropertyName("published_at")] DateTimeOffset PublishedAt);
}
