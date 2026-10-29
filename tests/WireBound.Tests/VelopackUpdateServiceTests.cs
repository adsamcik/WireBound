using System.Net;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json.Nodes;
using AwesomeAssertions;
using Microsoft.Extensions.Time.Testing;
using NSubstitute;
using Velopack;
using Velopack.Locators;
using Velopack.Logging;
using Velopack.Sources;
using WireBound.Avalonia.Services;

namespace WireBound.Tests.Services;

public class VelopackUpdateServiceTests
{
    [Test]
    [Arguments(23 * 60 + 59, false)]
    [Arguments(24 * 60, true)]
    [Arguments(24 * 60 + 1, true)]
    public async Task Updates_WaitForFull24Hours(int ageMinutes, bool eligible)
    {
        using var fixture = new UpdateFixture();
        fixture.AddRelease("2.0.0", TimeSpan.FromMinutes(ageMinutes));

        var portable = await fixture.Source.CheckPortableAsync("1.0.0", default);
        var feed = await fixture.GetFeedAsync();

        (portable is not null).Should().Be(eligible);
        feed.Assets.Should().HaveCount(eligible ? 1 : 0);
    }

    [Test]
    [Arguments("published_at")]
    [Arguments("created_at")]
    [Arguments("updated_at")]
    public async Task MissingOrFutureDates_BlockUpdates(string field)
    {
        using var fixture = new UpdateFixture();
        var release = fixture.AddRelease("2.0.0");
        var target = field == "published_at" ? release : release["assets"]![0]!.AsObject();
        foreach (var date in new string?[] { null, fixture.Clock.GetUtcNow().AddDays(1).ToString("O") })
        {
            target[field] = date;
            (await fixture.Source.CheckPortableAsync("1.0.0", default)).Should().BeNull();
            (await fixture.GetFeedAsync()).Assets.Should().BeEmpty();
        }
    }

    [Test]
    [Arguments("draft")]
    [Arguments("prerelease")]
    public async Task NonPublicStableReleases_AreExcluded(string flag)
    {
        using var fixture = new UpdateFixture();
        fixture.AddRelease("2.0.0")[flag] = true;
        (await fixture.GetFeedAsync()).Assets.Should().BeEmpty();
        (await fixture.Source.CheckPortableAsync("1.0.0", default)).Should().BeNull();
    }

    [Test]
    public async Task NewReleaseOrChangedFile_DoesNotHideOlderEligibleUpdate()
    {
        using var fixture = new UpdateFixture();
        fixture.AddRelease("10.0.0", TimeSpan.FromHours(1));
        var replaced = fixture.AddRelease("3.0.0");
        replaced["assets"]![1]!["updated_at"] = fixture.Clock.GetUtcNow().ToString("O");
        fixture.AddRelease("2.0.0");
        fixture.AddRelease("1.0.0");

        (await fixture.Source.CheckPortableAsync("1.0.0", default))!.Version.Should().Be("2.0.0");
        var feed = await fixture.GetFeedAsync();
        feed.Assets.Select(a => a.Version.ToString()).Should().BeEquivalentTo(["2.0.0", "1.0.0"]);
    }

    [Test]
    public async Task PortableChecks_CompareSemanticVersionsAndNeverDowngrade()
    {
        using var fixture = new UpdateFixture();
        fixture.AddRelease("2.0.0-beta.1");
        fixture.AddRelease("2.0.0");
        (await fixture.Source.CheckPortableAsync("2.0.0-beta.2", default))!.Version.Should().Be("2.0.0");
        (await fixture.Source.CheckPortableAsync("2.0.0", default)).Should().BeNull();
        (await fixture.Source.CheckPortableAsync("3.0.0", default)).Should().BeNull();
    }

    [Test]
    [Arguments("SHA256", "")]
    [Arguments("SHA256", "not-a-checksum")]
    [Arguments("PackageId", "OtherApp")]
    [Arguments("FileName", "../escape.nupkg")]
    [Arguments("Size", "999")]
    public async Task InvalidManifest_IsRejected(string field, string value)
    {
        using var fixture = new UpdateFixture();
        fixture.AddRelease("2.0.0");
        fixture.MutateManifest = asset => asset[field] = field == "Size" ? JsonValue.Create(long.Parse(value)) : JsonValue.Create(value);
        var check = () => fixture.GetFeedAsync();
        await check.Should().ThrowAsync<InvalidDataException>();
    }

    [Test]
    public async Task DigestMismatch_IsRejected()
    {
        using var fixture = new UpdateFixture();
        fixture.AddRelease("2.0.0")["assets"]![1]!["digest"] = $"sha256:{new string('0', 64)}";
        var check = () => fixture.GetFeedAsync();
        await check.Should().ThrowAsync<InvalidDataException>();
    }

    [Test]
    public async Task CumulativeManifest_OnlyUsesTheContainingReleasesVersion()
    {
        using var fixture = new UpdateFixture();
        fixture.AddRelease("2.0.0");
        fixture.AddPreviousManifestEntry = true;

        var feed = await fixture.GetFeedAsync();

        feed.Assets.Should().ContainSingle().Which.Version.ToString().Should().Be("2.0.0");
    }

    [Test]
    public async Task UntrustedDownloadUrl_IsRejected()
    {
        using var fixture = new UpdateFixture();
        fixture.AddRelease("2.0.0")["assets"]![1]!["browser_download_url"] = "https://evil.example/update.nupkg";
        var check = () => fixture.GetFeedAsync();
        await check.Should().ThrowAsync<InvalidDataException>();
    }

    [Test]
    [Arguments("removed")]
    [Arguments("draft")]
    [Arguments("replaced")]
    public async Task StaleRelease_IsBlockedBeforeDownloadAndInstall(string change)
    {
        using var fixture = new UpdateFixture();
        var release = fixture.AddRelease("2.0.0");
        var (service, manager, update) = await fixture.CreateServiceAsync();
        if (change == "removed") fixture.Releases.Clear();
        if (change == "draft") release["draft"] = true;
        if (change == "replaced") release["assets"]![1]!["id"] = 999;

        var download = () => service.DownloadUpdateAsync(update);
        var apply = () => service.ApplyUpdateAndRestartAsync(update);
        await download.Should().ThrowAsync<Exception>();
        await apply.Should().ThrowAsync<Exception>();
        manager.DownloadCount.Should().Be(0);
        manager.ApplyCount.Should().Be(0);
    }

    [Test]
    public async Task CachedUpdate_RequiresRemoteEligibilityAndChecksum()
    {
        using var fixture = new UpdateFixture();
        fixture.AddRelease("2.0.0");
        var (service, manager, update) = await fixture.CreateServiceAsync();
        var info = (UpdateInfo)update.NativeUpdateInfo!;
        manager.Pending = info.TargetFullRelease;

        await service.CheckForUpdateAsync();
        service.PreparedUpdate.Should().NotBeNull();

        manager.VerificationError = new InvalidDataException("corrupt");
        await service.CheckForUpdateAsync();
        service.PreparedUpdate.Should().BeNull();
        var apply = () => service.ApplyUpdateAndRestartAsync(update);
        await apply.Should().ThrowAsync<InvalidDataException>();
        manager.ApplyCount.Should().Be(0);
    }

    [Test]
    public async Task EligibleUpdate_DownloadsAndRevalidatesBeforeApplying()
    {
        using var fixture = new UpdateFixture();
        fixture.AddRelease("2.0.0");
        var (service, manager, update) = await fixture.CreateServiceAsync();
        await service.DownloadUpdateAsync(update);
        service.PreparedUpdate.Should().Be(update);
        var requestsBeforeInstall = fixture.RequestCount;

        await service.ApplyUpdateAndRestartAsync(update);

        fixture.RequestCount.Should().BeGreaterThan(requestsBeforeInstall);
        manager.VerifyCount.Should().Be(1);
        manager.ApplyCount.Should().Be(1);
    }

    [Test]
    public async Task FailedInstalledCheck_UsesSameGracePeriodForFallback()
    {
        using var fixture = new UpdateFixture();
        fixture.AddRelease("2.0.0");
        var (service, manager, _) = await fixture.CreateServiceAsync();
        fixture.AddRelease("3.0.0", TimeSpan.FromHours(1));
        manager.Check = () => Task.FromException<UpdateInfo?>(new HttpRequestException("feed unavailable"));

        var fallback = await service.CheckForUpdateAsync();

        fallback!.Version.Should().Be("2.0.0");
        fallback.CanInstallInApp.Should().BeFalse();
        fixture.Status = HttpStatusCode.ServiceUnavailable;
        var check = () => service.CheckForUpdateAsync();
        await check.Should().ThrowAsync<HttpRequestException>();
    }

    [Test]
    public async Task Cancellation_DoesNotBecomeFallbackOrPreparedUpdate()
    {
        using var fixture = new UpdateFixture();
        fixture.AddRelease("2.0.0");
        var (service, manager, update) = await fixture.CreateServiceAsync();
        using var cts = new CancellationTokenSource();
        var count = fixture.RequestCount;
        manager.Check = () =>
        {
            cts.Cancel();
            return Task.FromResult<UpdateInfo?>((UpdateInfo)update.NativeUpdateInfo!);
        };
        var check = () => service.CheckForUpdateAsync(cts.Token);
        await check.Should().ThrowAsync<OperationCanceledException>();
        fixture.RequestCount.Should().Be(count);
        service.PreparedUpdate.Should().BeNull();
    }

    [Test]
    public async Task LocalPackage_RejectsCorruptionAndCanRedownloadIt()
    {
        using var fixture = new UpdateFixture();
        fixture.AddRelease("2.0.0");
        var asset = (await fixture.GetFeedAsync()).Assets.Single();
        var directory = Path.Combine(Path.GetTempPath(), $"wirebound-update-test-{Guid.NewGuid():N}");
        Directory.CreateDirectory(directory);
        var file = Path.Combine(directory, asset.FileName);
        try
        {
            await File.WriteAllBytesAsync(file, Encoding.UTF8.GetBytes("corrupt"));
            var locator = Substitute.For<IVelopackLocator>();
            locator.PackagesDir.Returns(directory);
            locator.Log.Returns(Substitute.For<IVelopackLogger>());
            locator.AppId.Returns("WireBound");
            locator.CurrentlyInstalledVersion.Returns(SemanticVersion.Parse("1.0.0"));
            var manager = new VerifiedUpdateManager(fixture.Source, locator);
            var verify = () => manager.VerifyLocalPackageAsync(asset);
            await verify.Should().ThrowAsync<Exception>();
            (await File.ReadAllTextAsync(file)).Should().Be("corrupt");
            await File.WriteAllTextAsync(file, "package");
            await verify.Should().NotThrowAsync();
            await File.WriteAllTextAsync(file, "corrupt");
            await manager.DownloadUpdatesAsync(new UpdateInfo(asset, false));
            (await File.ReadAllTextAsync(file)).Should().Be("package");
        }
        finally
        {
            File.Delete(file);
            File.Delete(Path.Combine(directory, ".velopack_lock"));
            Directory.Delete(directory);
        }
    }

    private sealed class UpdateFixture : HttpMessageHandler
    {
        private static readonly byte[] PackageBytes = Encoding.UTF8.GetBytes("package");
        private static readonly string Hash = Convert.ToHexString(SHA256.HashData(PackageBytes));
        private readonly HttpClient _client;
        private readonly IFileDownloader _downloader = Substitute.For<IFileDownloader>();
        public FakeTimeProvider Clock { get; } = new(new DateTimeOffset(2026, 10, 29, 12, 0, 0, TimeSpan.Zero));
        public JsonArray Releases { get; } = [];
        public GitHubUpdateSource Source { get; }
        public Action<JsonObject>? MutateManifest { get; set; }
        public bool AddPreviousManifestEntry { get; set; }
        public HttpStatusCode Status { get; set; } = HttpStatusCode.OK;
        public int RequestCount { get; private set; }

        public UpdateFixture()
        {
            _client = new HttpClient(this, disposeHandler: false);
            Source = new GitHubUpdateSource(_client, Clock, _downloader);
            _downloader.DownloadFile(Arg.Any<string>(), Arg.Any<string>(), Arg.Any<Action<int>>(),
                    Arg.Any<IDictionary<string, string>?>(), Arg.Any<double>(), Arg.Any<CancellationToken>())
                .Returns(call => File.WriteAllBytesAsync(call.ArgAt<string>(1), PackageBytes, call.ArgAt<CancellationToken>(5)));
            _downloader.DownloadBytes(Arg.Any<string>(), Arg.Any<IDictionary<string, string>?>(), Arg.Any<double>())
                .Returns(call =>
                {
                    var version = ((string)call[0]).Split('/')[^2][1..];
                    var asset = new JsonObject
                    {
                        ["PackageId"] = "WireBound", ["Version"] = version, ["Type"] = "Full",
                        ["FileName"] = PackageName(version), ["Size"] = PackageBytes.Length,
                        ["SHA1"] = Convert.ToHexString(SHA1.HashData(PackageBytes)), ["SHA256"] = Hash
                    };
                    MutateManifest?.Invoke(asset);
                    var assets = new JsonArray(asset);
                    if (AddPreviousManifestEntry)
                    {
                        var previous = asset.DeepClone();
                        previous["Version"] = "1.0.0";
                        previous["FileName"] = PackageName("1.0.0");
                        assets.Add(previous);
                    }
                    return Encoding.UTF8.GetBytes(new JsonObject { ["Assets"] = assets }.ToJsonString());
                });
        }

        public JsonObject AddRelease(string version, TimeSpan? age = null)
        {
            var date = Clock.GetUtcNow() - (age ?? TimeSpan.FromDays(2));
            var release = new JsonObject
            {
                ["id"] = Releases.Count + 1, ["tag_name"] = $"v{version}", ["draft"] = false,
                ["prerelease"] = false, ["published_at"] = date.ToString("O"),
                ["assets"] = new JsonArray(Asset("releases.win-x64.json", version, date, 1), Asset(PackageName(version), version, date, 2))
            };
            Releases.Add(release);
            return release;
        }

        private static JsonObject Asset(string name, string version, DateTimeOffset date, int id) => new()
        {
            ["id"] = id, ["name"] = name, ["state"] = "uploaded", ["size"] = PackageBytes.Length,
            ["created_at"] = date.ToString("O"), ["updated_at"] = date.ToString("O"),
            ["browser_download_url"] = $"{GitHubUpdateSource.RepositoryUrl}/releases/download/v{version}/{name}",
            ["digest"] = $"sha256:{Hash}"
        };

        private static string PackageName(string version) => $"WireBound-{version}-full.nupkg";

        public Task<VelopackAssetFeed> GetFeedAsync() => Source.GetReleaseFeed(Substitute.For<IVelopackLogger>(), "WireBound", "win-x64");

        public async Task<(VelopackUpdateService Service, TestUpdateManager Manager, WireBound.Core.Services.UpdateCheckResult Update)> CreateServiceAsync()
        {
            var asset = (await GetFeedAsync()).Assets.Single();
            var locator = Substitute.For<IVelopackLocator>();
            locator.Log.Returns(Substitute.For<IVelopackLogger>());
            var manager = new TestUpdateManager(Source, locator)
            {
                Check = () => Task.FromResult<UpdateInfo?>(new UpdateInfo(asset, false))
            };
            var service = new VelopackUpdateService(manager, Source);
            return (service, manager, (await service.CheckForUpdateAsync())!);
        }

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            RequestCount++;
            var tag = request.RequestUri!.Segments.Last().Trim('/');
            var isTagRequest = request.RequestUri.AbsolutePath.Contains("/tags/");
            JsonNode? data = isTagRequest ? Releases.FirstOrDefault(r => r!["tag_name"]!.GetValue<string>() == tag) : Releases;
            return Task.FromResult(new HttpResponseMessage(data is null ? HttpStatusCode.NotFound : Status)
            {
                Content = new StringContent(data?.ToJsonString() ?? "null")
            });
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing) _client.Dispose();
            base.Dispose(disposing);
        }
    }

    private sealed class TestUpdateManager(IUpdateSource source, IVelopackLocator locator) : VerifiedUpdateManager(source, locator)
    {
        public Func<Task<UpdateInfo?>> Check { get; set; } = () => Task.FromResult<UpdateInfo?>(null);
        public VelopackAsset? Pending { get; set; }
        public Exception? VerificationError { get; set; }
        public int DownloadCount { get; private set; }
        public int VerifyCount { get; private set; }
        public int ApplyCount { get; private set; }
        public override bool IsInstalled => true;
        public override SemanticVersion CurrentVersion => SemanticVersion.Parse("1.0.0");
        public override VelopackAsset? UpdatePendingRestart => Pending;
        public override Task<UpdateInfo?> CheckForUpdatesAsync() => Check();
        public override Task DownloadUpdatesAsync(UpdateInfo updates, Action<int>? progress = null, CancellationToken cancelToken = default)
        {
            DownloadCount++;
            return Task.CompletedTask;
        }
        public override Task VerifyLocalPackageAsync(VelopackAsset asset)
        {
            VerifyCount++;
            return VerificationError is null ? Task.CompletedTask : Task.FromException(VerificationError);
        }
        public override void ApplyAndRestart(VelopackAsset asset) => ApplyCount++;
    }
}
