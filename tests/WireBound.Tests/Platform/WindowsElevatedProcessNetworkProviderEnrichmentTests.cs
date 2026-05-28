using System.Runtime.Versioning;
using WireBound.IPC.Messages;
using WireBound.Platform.Abstract.Helpers;
using WireBound.Platform.Abstract.Services;
using WireBound.Platform.Windows.Services;

namespace WireBound.Tests.Platform;

/// <summary>
/// Regression coverage for the enrichment + speed-delta logic that lives
/// inside <see cref="WindowsElevatedProcessNetworkProvider"/>.
/// </summary>
/// <remarks>
/// <para>
/// Without this enrichment the provider used to emit
/// <see cref="WireBound.Platform.Abstract.Models.ProcessNetworkStats"/> with
/// empty <c>AppIdentifier</c> values; <c>DataPersistenceService.SaveAppStatsAsync</c>
/// then silently dropped every record, so the Applications tab stayed at
/// "0 apps tracked" indefinitely even with the elevated helper connected.
/// </para>
/// <para>
/// These tests pin: identifier derivation from the helper-supplied executable
/// path, deterministic display-name resolution, speed deltas computed against
/// the previous sample, and the PID-reuse / counter-decrease guard that
/// prevents wrap-around speeds when a process restarts.
/// </para>
/// </remarks>
[SupportedOSPlatform("windows")]
public class WindowsElevatedProcessNetworkProviderEnrichmentTests
{
    private static WindowsElevatedProcessNetworkProvider CreateProvider()
        => new(Substitute.For<IHelperConnection>());

    [Test]
    public void EnrichAndComputeSpeeds_PopulatesAppIdentifierFromExecutablePath()
    {
        var provider = CreateProvider();
        var sample = new ProcessByteStats
        {
            ProcessId = 1234,
            ProcessName = "firefox",
            ExecutablePath = "C:\\Program Files\\Mozilla Firefox\\firefox.exe",
            TotalBytesReceived = 1000,
            TotalBytesSent = 500
        };

        var enriched = provider.EnrichAndComputeSpeeds([sample], DateTime.Now);

        enriched.Should().HaveCount(1);
        enriched[0].AppIdentifier.Should().Be(
            AppIdentity.ComputeAppIdentifier("C:\\Program Files\\Mozilla Firefox\\firefox.exe"));
        enriched[0].AppIdentifier.Should().NotBeNullOrEmpty(
            because: "DataPersistenceService drops records with empty AppIdentifier");
    }

    [Test]
    public void EnrichAndComputeSpeeds_PopulatesDisplayNameFromExecutablePath()
    {
        var provider = CreateProvider();
        var sample = new ProcessByteStats
        {
            ProcessId = 1234,
            ProcessName = "firefox",
            ExecutablePath = "C:\\Program Files\\Mozilla Firefox\\firefox.exe"
        };

        var enriched = provider.EnrichAndComputeSpeeds([sample], DateTime.Now);

        enriched[0].DisplayName.Should().Be("firefox");
        enriched[0].ExecutablePath.Should().Be("C:\\Program Files\\Mozilla Firefox\\firefox.exe");
    }

    [Test]
    public void EnrichAndComputeSpeeds_WithEmptyExecutablePath_StillProducesAppIdentifier()
    {
        var provider = CreateProvider();
        var sample = new ProcessByteStats
        {
            ProcessId = 4,
            ProcessName = "System",
            ExecutablePath = string.Empty,
            TotalBytesReceived = 100
        };

        var enriched = provider.EnrichAndComputeSpeeds([sample], DateTime.Now);

        enriched.Should().HaveCount(1);
        enriched[0].AppIdentifier.Should().Be(AppIdentity.UnknownIdentifier,
            because: "missing path must still produce a non-empty identifier or persistence drops the row");
        enriched[0].DisplayName.Should().Be("System");
    }

    [Test]
    public void EnrichAndComputeSpeeds_FirstSample_ReportsZeroSpeed()
    {
        var provider = CreateProvider();
        var sample = new ProcessByteStats
        {
            ProcessId = 1234,
            ProcessName = "firefox",
            ExecutablePath = "C:\\firefox.exe",
            TotalBytesReceived = 1_000_000,
            TotalBytesSent = 500_000
        };

        var enriched = provider.EnrichAndComputeSpeeds([sample], DateTime.Now);

        enriched[0].DownloadSpeedBps.Should().Be(0,
            because: "no prior sample means no delta — emitting raw cumulative bytes as speed would be wrong");
        enriched[0].UploadSpeedBps.Should().Be(0);
        enriched[0].SessionBytesReceived.Should().Be(1_000_000);
        enriched[0].SessionBytesSent.Should().Be(500_000);
    }

    [Test]
    public void EnrichAndComputeSpeeds_SecondSample_ComputesSpeedFromDelta()
    {
        var provider = CreateProvider();
        var t0 = new DateTime(2026, 5, 28, 17, 0, 0);
        var t1 = t0.AddSeconds(2);

        provider.EnrichAndComputeSpeeds(
            [new ProcessByteStats
            {
                ProcessId = 1234,
                ProcessName = "firefox",
                ExecutablePath = "C:\\firefox.exe",
                TotalBytesReceived = 1_000_000,
                TotalBytesSent = 500_000
            }],
            t0);

        var enriched = provider.EnrichAndComputeSpeeds(
            [new ProcessByteStats
            {
                ProcessId = 1234,
                ProcessName = "firefox",
                ExecutablePath = "C:\\firefox.exe",
                TotalBytesReceived = 1_200_000,  // +200 KB in 2s = 100 KB/s
                TotalBytesSent = 700_000          // +200 KB in 2s = 100 KB/s
            }],
            t1);

        enriched[0].DownloadSpeedBps.Should().Be(100_000);
        enriched[0].UploadSpeedBps.Should().Be(100_000);
    }

    [Test]
    public void EnrichAndComputeSpeeds_CountersDecrease_TreatedAsRestartAndReportsZero()
    {
        var provider = CreateProvider();
        var t0 = new DateTime(2026, 5, 28, 17, 0, 0);
        var t1 = t0.AddSeconds(2);

        provider.EnrichAndComputeSpeeds(
            [new ProcessByteStats
            {
                ProcessId = 1234,
                ProcessName = "firefox",
                ExecutablePath = "C:\\firefox.exe",
                TotalBytesReceived = 1_000_000,
                TotalBytesSent = 500_000
            }],
            t0);

        // Counter resets (process restart or PID reuse) — emitting (curr - prev)
        // would yield a huge negative speed; the provider must clamp to zero.
        var enriched = provider.EnrichAndComputeSpeeds(
            [new ProcessByteStats
            {
                ProcessId = 1234,
                ProcessName = "firefox",
                ExecutablePath = "C:\\firefox.exe",
                TotalBytesReceived = 10_000,
                TotalBytesSent = 5_000
            }],
            t1);

        enriched[0].DownloadSpeedBps.Should().Be(0);
        enriched[0].UploadSpeedBps.Should().Be(0);
    }

    [Test]
    public void EnrichAndComputeSpeeds_TracksFirstSeenAcrossPolls()
    {
        var provider = CreateProvider();
        var t0 = new DateTime(2026, 5, 28, 17, 0, 0);
        var t1 = t0.AddSeconds(5);

        var firstSample = new ProcessByteStats
        {
            ProcessId = 1234,
            ProcessName = "firefox",
            ExecutablePath = "C:\\firefox.exe"
        };

        var first = provider.EnrichAndComputeSpeeds([firstSample], t0);
        var second = provider.EnrichAndComputeSpeeds([firstSample], t1);

        first[0].FirstSeen.Should().Be(t0);
        second[0].FirstSeen.Should().Be(t0, because: "FirstSeen is fixed at the first observation");
        second[0].LastSeen.Should().Be(t1);
    }

    [Test]
    public void EnrichAndComputeSpeeds_PidThatDisappears_IsEvictedFromState()
    {
        var provider = CreateProvider();
        var t0 = new DateTime(2026, 5, 28, 17, 0, 0);
        var t1 = t0.AddSeconds(2);
        var t2 = t1.AddSeconds(2);

        // PID 1234 appears at t0...
        provider.EnrichAndComputeSpeeds(
            [new ProcessByteStats { ProcessId = 1234, ProcessName = "firefox", ExecutablePath = "C:\\firefox.exe", TotalBytesReceived = 1000 }],
            t0);
        // ...disappears at t1 (only PID 5678 reported)...
        provider.EnrichAndComputeSpeeds(
            [new ProcessByteStats { ProcessId = 5678, ProcessName = "chrome", ExecutablePath = "C:\\chrome.exe", TotalBytesReceived = 2000 }],
            t1);
        // ...and reappears at t2 with a lower cumulative count (process restarted).
        var enriched = provider.EnrichAndComputeSpeeds(
            [new ProcessByteStats { ProcessId = 1234, ProcessName = "firefox", ExecutablePath = "C:\\firefox.exe", TotalBytesReceived = 100 }],
            t2);

        enriched.Should().HaveCount(1);
        enriched[0].DownloadSpeedBps.Should().Be(0,
            because: "the previous PID-1234 sample should have been evicted, so this counts as a fresh first sample");
        enriched[0].FirstSeen.Should().Be(t2);
    }
}
