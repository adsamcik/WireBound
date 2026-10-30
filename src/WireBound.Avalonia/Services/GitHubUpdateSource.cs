using System.Net.Http;
using System.Text.Json;
using System.Text.Json.Serialization;
using Velopack;
using Velopack.Logging;
using Velopack.Sources;
using WireBound.Core.Services;

namespace WireBound.Avalonia.Services;

/// <summary>Only exposes stable releases whose publication and files have aged at least 24 hours.</summary>
internal sealed partial class GitHubUpdateSource(HttpClient httpClient, TimeProvider timeProvider, IFileDownloader? downloader = null)
    : GithubSource(RepositoryUrl, accessToken: null, prerelease: false, downloader)
{
    internal const string RepositoryUrl = "https://github.com/adsamcik/WireBound";
    private const string ApiUrl = "https://api.github.com/repos/adsamcik/WireBound/releases";
    internal static readonly TimeSpan GracePeriod = TimeSpan.FromHours(24);

    protected override async Task<GithubRelease[]> GetReleases(bool includePrereleases)
    {
        var releases = await GetReleaseListAsync(CancellationToken.None).ConfigureAwait(false);
        return releases.Where(IsEligible).Select(release => new EligibleRelease
        {
            Metadata = release,
            Name = release.TagName,
            PublishedAt = release.PublishedAt!.Value.UtcDateTime,
            Assets = release.Assets!.Select(asset => new GithubReleaseAsset
            {
                Name = asset.Name,
                BrowserDownloadUrl = asset.DownloadUrl
            }).ToArray()
        }).ToArray<GithubRelease>();
    }

    internal async Task<UpdateCheckResult?> CheckPortableAsync(string currentVersion, CancellationToken cancellationToken)
    {
        if (!SemanticVersion.TryParse(currentVersion, out var current))
            throw new InvalidDataException("The installed version could not be parsed.");

        var releases = await GetReleaseListAsync(cancellationToken).ConfigureAwait(false);
        var release = releases.Where(IsEligible)
            .Where(r => ParseVersion(r.TagName) > current)
            .MaxBy(r => ParseVersion(r.TagName));
        return release is null ? null : new UpdateCheckResult(
            ParseVersion(release.TagName)!.ToString(), ReleaseUrl(release.TagName!), release.PublishedAt, null);
    }

    public override async Task<VelopackAssetFeed> GetReleaseFeed(IVelopackLogger logger, string? appId,
        string channel, Guid? stagingId = null, VelopackAsset? latestLocalRelease = null)
    {
        var feed = await base.GetReleaseFeed(logger, appId, channel, stagingId, latestLocalRelease).ConfigureAwait(false);
        // Velopack manifests include previous packages used to build deltas. Only a tag's own
        // version belongs here; older packages must pass the policy through their own release.
        feed.Assets = feed.Assets.Where(asset => asset.Version == ParseVersion(GetMetadata(asset).TagName)).ToArray();
        foreach (var asset in feed.Assets)
        {
            var release = GetMetadata(asset);
            ValidateAsset(asset, release);
        }
        return feed;
    }

    internal DateTimeOffset GetPublishedAt(VelopackAsset asset) => GetMetadata(asset).PublishedAt!.Value;
    internal string GetReleaseUrl(VelopackAsset asset) => ReleaseUrl(GetMetadata(asset).TagName!);

    public override async Task DownloadReleaseEntry(IVelopackLogger logger, VelopackAsset releaseEntry,
        string localFile, Action<int> progress, CancellationToken cancelToken)
    {
        await RevalidateAsync(releaseEntry, cancelToken).ConfigureAwait(false);
        await base.DownloadReleaseEntry(logger, releaseEntry, localFile, progress, cancelToken).ConfigureAwait(false);
    }

    // Fetch by tag again so deleted, replaced, or withdrawn releases cannot be installed from stale UI state.
    internal async Task RevalidateAsync(VelopackAsset asset, CancellationToken cancellationToken)
    {
        var original = GetMetadata(asset);
        using var response = await httpClient.GetAsync(
            $"{ApiUrl}/tags/{Uri.EscapeDataString(original.TagName!)}", cancellationToken).ConfigureAwait(false);
        response.EnsureSuccessStatusCode();
        var json = await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);
        var current = JsonSerializer.Deserialize(json, UpdateJsonContext.Default.ReleaseMetadata)
            ?? throw new InvalidDataException("GitHub returned an invalid release response.");
        if (current.Id != original.Id || current.TagName != original.TagName || !IsEligible(current))
            throw new InvalidOperationException("This release is unavailable or still in its 24-hour waiting period. Check for updates again.");

        ValidateAsset(asset, current);
        var oldFile = original.Assets!.Single(a => a.Name == asset.FileName);
        var newFile = current.Assets!.Single(a => a.Name == asset.FileName);
        if (oldFile != newFile)
            throw new InvalidOperationException("The update files changed. Check for updates again.");
    }

    protected override string GetAssetUrlFromName(GithubRelease release, string assetName)
    {
        var url = base.GetAssetUrlFromName(release, assetName);
        if (release is not EligibleRelease eligible || !IsTrustedAssetUrl(url, eligible.Metadata.TagName!, assetName))
            throw new InvalidDataException("The update download URL does not belong to WireBound's GitHub release.");
        return url;
    }

    private async Task<ReleaseMetadata[]> GetReleaseListAsync(CancellationToken cancellationToken)
    {
        // Bounded history allows an older eligible update while a newer release is waiting.
        using var response = await httpClient.GetAsync($"{ApiUrl}?per_page=100", cancellationToken).ConfigureAwait(false);
        response.EnsureSuccessStatusCode();
        var json = await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);
        return JsonSerializer.Deserialize(json, UpdateJsonContext.Default.ReleaseMetadataArray)
            ?? throw new InvalidDataException("GitHub returned an invalid release list.");
    }

    private bool IsEligible(ReleaseMetadata release)
    {
        var cutoff = timeProvider.GetUtcNow() - GracePeriod;
        return release.Id > 0 && !release.Draft && !release.Prerelease &&
            ParseVersion(release.TagName) is { IsPrerelease: false } &&
            IsOldEnough(release.PublishedAt, cutoff) && release.Assets is { Length: > 0 } &&
            release.Assets.All(a => a.Id > 0 && a.State == "uploaded" &&
                IsOldEnough(a.CreatedAt, cutoff) && IsOldEnough(a.UpdatedAt, cutoff));
    }

    private static bool IsOldEnough(DateTimeOffset? timestamp, DateTimeOffset cutoff) =>
        timestamp is { } value && value > DateTimeOffset.UnixEpoch && value <= cutoff;

    private static SemanticVersion? ParseVersion(string? tag) =>
        SemanticVersion.TryParse(tag?.StartsWith('v') == true ? tag[1..] : tag, out var version) ? version : null;

    private static ReleaseMetadata GetMetadata(VelopackAsset asset) =>
        asset is GitBaseAsset { Release: EligibleRelease release } ? release.Metadata :
            throw new ArgumentException("The update was not obtained from the trusted release source.", nameof(asset));

    private static void ValidateAsset(VelopackAsset asset, ReleaseMetadata release)
    {
        var files = release.Assets?.Where(a => a.Name == asset.FileName).ToArray();
        if (asset.PackageId != "WireBound" || asset.Version != ParseVersion(release.TagName) ||
            string.IsNullOrEmpty(asset.FileName) || asset.FileName.IndexOfAny(['/', '\\', ':']) >= 0 ||
            asset.FileName is "." or ".." || asset.Size <= 0 ||
            asset.SHA256 is not { Length: 64 } || !asset.SHA256.All(Uri.IsHexDigit) ||
            files is not { Length: 1 } || files[0].Size != asset.Size ||
            !IsTrustedAssetUrl(files[0].DownloadUrl, release.TagName!, asset.FileName) ||
            (files[0].Digest is { Length: > 0 } digest &&
                !string.Equals(digest, $"sha256:{asset.SHA256}", StringComparison.OrdinalIgnoreCase)))
            throw new InvalidDataException("The update package identity, size, or SHA-256 checksum is invalid.");
    }

    private static bool IsTrustedAssetUrl(string? url, string tag, string fileName) =>
        string.Equals(url, $"{RepositoryUrl}/releases/download/{Uri.EscapeDataString(tag)}/{Uri.EscapeDataString(fileName)}",
            StringComparison.Ordinal);

    internal static string ReleaseUrl(string tag) => $"{RepositoryUrl}/releases/tag/{Uri.EscapeDataString(tag)}";

    private sealed class EligibleRelease : GithubRelease
    {
        public required ReleaseMetadata Metadata { get; init; }
    }

    private sealed record ReleaseMetadata(
        [property: JsonPropertyName("id")] long Id,
        [property: JsonPropertyName("tag_name")] string? TagName,
        [property: JsonPropertyName("draft")] bool Draft,
        [property: JsonPropertyName("prerelease")] bool Prerelease,
        [property: JsonPropertyName("published_at")] DateTimeOffset? PublishedAt,
        [property: JsonPropertyName("assets")] AssetMetadata[]? Assets);

    private sealed record AssetMetadata(
        [property: JsonPropertyName("id")] long Id,
        [property: JsonPropertyName("name")] string? Name,
        [property: JsonPropertyName("state")] string? State,
        [property: JsonPropertyName("size")] long Size,
        [property: JsonPropertyName("created_at")] DateTimeOffset? CreatedAt,
        [property: JsonPropertyName("updated_at")] DateTimeOffset? UpdatedAt,
        [property: JsonPropertyName("browser_download_url")] string? DownloadUrl,
        [property: JsonPropertyName("digest")] string? Digest);

    [JsonSerializable(typeof(ReleaseMetadata[]))]
    [JsonSerializable(typeof(ReleaseMetadata))]
    private partial class UpdateJsonContext : JsonSerializerContext;
}
