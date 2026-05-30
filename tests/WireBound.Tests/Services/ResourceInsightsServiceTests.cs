using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Time.Testing;
using WireBound.Avalonia.Services;
using WireBound.Core.Data;
using WireBound.Core.Models;
using WireBound.Core.Services;
using WireBound.Platform.Abstract.Models;
using WireBound.Platform.Abstract.Services;
using WireBound.Tests.Fixtures;

namespace WireBound.Tests.Services;

public class ResourceInsightsServiceTests : DatabaseTestBase
{
    private const long TenMb = 10L * 1024 * 1024;
    private const long TwentyMb = 20L * 1024 * 1024;
    private const long TwoHundredMb = 200L * 1024 * 1024;
    private static readonly TimeSpan SnapshotInterval = TimeSpan.FromSeconds(1);

    private (ResourceInsightsService service, IProcessResourceProvider provider, FakeTimeProvider fakeTime) CreateService(
        ILogger<ResourceInsightsService>? logger = null)
    {
        var provider = Substitute.For<IProcessResourceProvider>();
        var categoryService = Substitute.For<IAppCategoryService>();
        categoryService.GetCategory(Arg.Any<string>()).Returns("Other");
        categoryService.GetCategory("chrome").Returns("Web Browsers");
        categoryService.GetCategory("code").Returns("Development Tools");
        // Also configure the new overload used by ResourceInsightsService
        categoryService.GetCategory(Arg.Any<string>(), Arg.Any<string?>(), Arg.Any<int>()).Returns("Other");
        categoryService.GetCategory("chrome", Arg.Any<string?>(), Arg.Any<int>()).Returns("Web Browsers");
        categoryService.GetCategory("code", Arg.Any<string?>(), Arg.Any<int>()).Returns("Development Tools");

        var fakeTime = new FakeTimeProvider();
        var service = new ResourceInsightsService(provider, categoryService, ServiceProvider, fakeTime, logger);
        return (service, provider, fakeTime);
    }

    [Test]
    public async Task GetCurrentByAppAsync_GroupsByExecutablePath()
    {
        var (service, provider, _) = CreateService();

        // Simulate 3 chrome processes
        provider.GetProcessResourceDataAsync(Arg.Any<CancellationToken>())
            .Returns(new List<ProcessResourceData>
            {
                new() { ProcessId = 1, ProcessName = "chrome", ExecutablePath = "chrome.exe", PrivateBytes = 500_000_000, CpuTimeTicks = 1000 },
                new() { ProcessId = 2, ProcessName = "chrome", ExecutablePath = "chrome.exe", PrivateBytes = 300_000_000, CpuTimeTicks = 500 },
                new() { ProcessId = 3, ProcessName = "code", ExecutablePath = "code.exe", PrivateBytes = 200_000_000, CpuTimeTicks = 200 },
            });

        var apps = await service.GetCurrentByAppAsync();

        apps.Count.Should().Be(2);
        var chrome = apps.FirstOrDefault(a => a.AppName == "chrome");
        chrome.Should().NotBeNull();
        chrome!.ProcessCount.Should().Be(2);
        // First call: no EMA smoothing delta, so private bytes = raw sum
        chrome.PrivateBytes.Should().Be(800_000_000);
    }

    [Test]
    public async Task GetCurrentByAppAsync_ReturnsEmptyForNoProcesses()
    {
        var (service, provider, _) = CreateService();

        provider.GetProcessResourceDataAsync(Arg.Any<CancellationToken>())
            .Returns(new List<ProcessResourceData>());

        var apps = await service.GetCurrentByAppAsync();

        apps.Count.Should().Be(0);
    }

    [Test]
    public async Task GetCategoryBreakdown_GroupsByCategory()
    {
        var (service, provider, _) = CreateService();

        provider.GetProcessResourceDataAsync(Arg.Any<CancellationToken>())
            .Returns(new List<ProcessResourceData>
            {
                new() { ProcessId = 1, ProcessName = "chrome", ExecutablePath = "chrome.exe", PrivateBytes = 500_000_000, CpuTimeTicks = 1000 },
                new() { ProcessId = 2, ProcessName = "code", ExecutablePath = "code.exe", PrivateBytes = 200_000_000, CpuTimeTicks = 200 },
            });

        var apps = await service.GetCurrentByAppAsync();
        var categories = service.GetCategoryBreakdown(apps);

        categories.Count.Should().BeGreaterThanOrEqualTo(2);
    }

    [Test]
    public async Task CpuPercent_SecondCallComputesDelta()
    {
        var (service, provider, fakeTime) = CreateService();

        // First call: establishes baseline
        provider.GetProcessResourceDataAsync(Arg.Any<CancellationToken>())
            .Returns(new List<ProcessResourceData>
            {
                new() { ProcessId = 1, ProcessName = "busy", ExecutablePath = "busy.exe", PrivateBytes = 100_000_000, CpuTimeTicks = 0 },
            });

        var first = await service.GetCurrentByAppAsync();
        first.First().CpuPercent.Should().Be(0); // No delta on first call

        // Second call: simulate CPU work (1 second of full-core CPU time in ticks)
        provider.GetProcessResourceDataAsync(Arg.Any<CancellationToken>())
            .Returns(new List<ProcessResourceData>
            {
                new() { ProcessId = 1, ProcessName = "busy", ExecutablePath = "busy.exe", PrivateBytes = 100_000_000, CpuTimeTicks = TimeSpan.TicksPerSecond },
            });

        fakeTime.Advance(TimeSpan.FromMilliseconds(100));

        var second = await service.GetCurrentByAppAsync();
        // CPU% should be > 0 now since there's a delta
        second.First().CpuPercent.Should().BeGreaterThan(0);
    }

    [Test]
    public async Task Smoothing_SecondCallAppliesEma()
    {
        var (service, provider, _) = CreateService();

        // First call: 1 GB
        provider.GetProcessResourceDataAsync(Arg.Any<CancellationToken>())
            .Returns(new List<ProcessResourceData>
            {
                new() { ProcessId = 1, ProcessName = "app", ExecutablePath = "app.exe", PrivateBytes = 1_000_000_000, CpuTimeTicks = 0 },
            });

        var first = await service.GetCurrentByAppAsync();
        var firstBytes = first.First().PrivateBytes;
        firstBytes.Should().Be(1_000_000_000); // First sample = raw value

        // Second call: spike to 2 GB — EMA should smooth it (not jump to 2 GB instantly)
        provider.GetProcessResourceDataAsync(Arg.Any<CancellationToken>())
            .Returns(new List<ProcessResourceData>
            {
                new() { ProcessId = 1, ProcessName = "app", ExecutablePath = "app.exe", PrivateBytes = 2_000_000_000, CpuTimeTicks = 0 },
            });

        var second = await service.GetCurrentByAppAsync();
        var secondBytes = second.First().PrivateBytes;
        // With α_up=0.3: smoothed = 1_000_000_000 * 0.7 + 2_000_000_000 * 0.3 = 1_300_000_000
        secondBytes.Should().Be(1_300_000_000);
    }

    [Test]
    public async Task GetRollingCpuByApp_NoCalls_ReturnsEmpty()
    {
        var (service, _, _) = CreateService();

        var result = service.GetRollingCpuByApp(TimeSpan.FromSeconds(60));

        result.Should().BeEmpty();
    }

    [Test]
    public async Task GetRollingCpuByApp_RecordsSamplesAcrossMultiplePolls()
    {
        var (service, provider, fakeTime) = CreateService();

        // First poll: baseline (CPU% = 0 since there's no delta yet).
        SetProcessSnapshots(provider, [CreateProcess("worker", TenMb, TenMb, 0)]);
        await service.GetCurrentByAppAsync();

        // Subsequent polls: enough CPU work to register 4% per tick.
        var ticks = CpuTicksForPercent(4);
        for (int i = 1; i <= 4; i++)
        {
            SetProcessSnapshots(provider, [CreateProcess("worker", TenMb, TenMb, ticks * i)]);
            fakeTime.Advance(SnapshotInterval);
            await service.GetCurrentByAppAsync();
        }

        var rolling = service.GetRollingCpuByApp(TimeSpan.FromSeconds(60));
        rolling.Should().ContainSingle();
        var appId = rolling.Keys.Single();
        // Smoothed CPU% over the recorded samples should be in single digits
        // (started at 0, climbed via EMA toward 4%). We only assert > 0 to
        // avoid pinning to specific EMA math.
        rolling[appId].Should().BeGreaterThan(0);
        rolling[appId].Should().BeLessThan(10);
    }

    [Test]
    public async Task GetRollingCpuByApp_SamplesOutsideWindowAreExcluded()
    {
        var (service, provider, fakeTime) = CreateService();

        // Poll 1 at t=0 (baseline, raw CPU=0, smoothed=0).
        SetProcessSnapshots(provider, [CreateProcess("worker", TenMb, TenMb, 0)]);
        await service.GetCurrentByAppAsync();
        fakeTime.Advance(TimeSpan.FromSeconds(30));

        // Poll 2 at t=30 — registers some CPU work, smoothed → ~3.2%.
        SetProcessSnapshots(provider, [CreateProcess("worker", TenMb, TenMb, CpuTicksForPercent(8) * 30)]);
        var poll2 = await service.GetCurrentByAppAsync();
        var poll2Cpu = poll2.Single().CpuPercent;
        poll2Cpu.Should().BeGreaterThan(0);

        // Advance 120s so poll 2's sample (at t=30) leaves the 60s window
        // when read at t=150 (window covers t=90..t=150).
        fakeTime.Advance(TimeSpan.FromSeconds(120));

        // Poll 3 at t=150 — no new CPU work, raw=0 but EMA decays slowly.
        SetProcessSnapshots(provider, [CreateProcess("worker", TenMb, TenMb, CpuTicksForPercent(8) * 30)]);
        var poll3 = await service.GetCurrentByAppAsync();
        var poll3Cpu = poll3.Single().CpuPercent;

        var rolling = service.GetRollingCpuByApp(TimeSpan.FromSeconds(60));
        rolling.Should().ContainSingle();
        // Only the t=150 sample is in window — its value should match poll 3
        // exactly (no other samples to average with).
        rolling.Values.Single().Should().BeApproximately(poll3Cpu, 0.001);
    }

    [Test]
    public void GetRollingCpuByApp_NonPositiveWindow_Throws()
    {
        var (service, _, _) = CreateService();

        var act = () => service.GetRollingCpuByApp(TimeSpan.Zero);
        act.Should().Throw<ArgumentOutOfRangeException>();
    }

    [Test]
    public async Task RecordSnapshotAsync_AppBelowBothThresholds_IsSkipped()
    {
        var (service, provider, fakeTime) = CreateService();
        SetProcessSnapshots(
            provider,
            [CreateProcess("idle", TwentyMb, TwentyMb, 0)],
            [CreateProcess("idle", TwentyMb, TwentyMb, CpuTicksForPercent(0.05))]);

        await service.RecordSnapshotAsync();
        fakeTime.Advance(SnapshotInterval);
        await service.RecordSnapshotAsync();

        (await CountSnapshotsAsync()).Should().Be(0);
    }

    [Test]
    public async Task RecordSnapshotAsync_AppAboveCpuThreshold_IsPersisted()
    {
        var (service, provider, fakeTime) = CreateService();
        SetProcessSnapshots(
            provider,
            [CreateProcess("cpu-heavy", TenMb, TenMb, 0)],
            [CreateProcess("cpu-heavy", TenMb, TenMb, CpuTicksForPercent(5))]);

        await service.RecordSnapshotAsync();
        fakeTime.Advance(SnapshotInterval);
        await service.RecordSnapshotAsync();

        var snapshots = await GetSnapshotsAsync();
        snapshots.Should().ContainSingle();
        snapshots.Single().WorkingSetBytes.Should().Be(TenMb);
    }

    [Test]
    public async Task RecordSnapshotAsync_AppAboveRamThreshold_IsPersisted()
    {
        var (service, provider, _) = CreateService();
        SetProcessSnapshots(provider, [CreateProcess("memory-heavy", TenMb, TwoHundredMb, 0)]);

        await service.RecordSnapshotAsync();

        var snapshots = await GetSnapshotsAsync();
        snapshots.Should().ContainSingle();
        snapshots.Single().WorkingSetBytes.Should().Be(TwoHundredMb);
    }

    [Test]
    public async Task RecordSnapshotAsync_MixedBatch_OnlyHighActivityPersisted()
    {
        var (service, provider, fakeTime) = CreateService();
        SetProcessSnapshots(
            provider,
            [
                CreateProcess("idle", TwentyMb, TwentyMb, 0),
                CreateProcess("cpu-heavy", TenMb, TenMb, 0),
                CreateProcess("memory-heavy", TenMb, TwoHundredMb, 0)
            ],
            [
                CreateProcess("idle", TwentyMb, TwentyMb, CpuTicksForPercent(0.05)),
                CreateProcess("cpu-heavy", TenMb, TenMb, CpuTicksForPercent(5)),
                CreateProcess("memory-heavy", TenMb, TwoHundredMb, CpuTicksForPercent(0.01))
            ]);

        await service.RecordSnapshotAsync();
        fakeTime.Advance(SnapshotInterval);
        await service.RecordSnapshotAsync();

        var snapshots = await GetSnapshotsAsync();
        snapshots.Should().HaveCount(2);
        snapshots.Select(s => s.AppName).Should().BeEquivalentTo("cpu-heavy", "memory-heavy");
    }

    [Test]
    public async Task RecordSnapshotAsync_LogsKeptAndSkippedCounts()
    {
        var logger = new ListLogger<ResourceInsightsService>();
        var (service, provider, fakeTime) = CreateService(logger);
        SetProcessSnapshots(
            provider,
            [
                CreateProcess("idle", TwentyMb, TwentyMb, 0),
                CreateProcess("cpu-heavy", TenMb, TenMb, 0),
                CreateProcess("memory-heavy", TenMb, TwoHundredMb, 0)
            ],
            [
                CreateProcess("idle", TwentyMb, TwentyMb, CpuTicksForPercent(0.05)),
                CreateProcess("cpu-heavy", TenMb, TenMb, CpuTicksForPercent(5)),
                CreateProcess("memory-heavy", TenMb, TwoHundredMb, CpuTicksForPercent(0.01))
            ]);

        await service.RecordSnapshotAsync();
        logger.Entries.Clear();
        fakeTime.Advance(SnapshotInterval);

        await service.RecordSnapshotAsync();

        logger.Entries.Should().ContainSingle(entry =>
            entry.Level == LogLevel.Debug &&
            entry.Message == "ResourceInsights snapshot: persisted 2 apps, skipped 1 low-activity apps");
    }

    private async Task<int> CountSnapshotsAsync()
    {
        using var scope = CreateScope();
        var context = scope.ServiceProvider.GetRequiredService<WireBoundDbContext>();
        return await context.ResourceInsightSnapshots.CountAsync();
    }

    private async Task<List<ResourceInsightSnapshot>> GetSnapshotsAsync()
    {
        using var scope = CreateScope();
        var context = scope.ServiceProvider.GetRequiredService<WireBoundDbContext>();
        return await context.ResourceInsightSnapshots.OrderBy(s => s.AppName).ToListAsync();
    }

    private static void SetProcessSnapshots(
        IProcessResourceProvider provider,
        params IReadOnlyList<ProcessResourceData>[] snapshots)
    {
        var queued = new Queue<IReadOnlyList<ProcessResourceData>>(snapshots);
        provider.GetProcessResourceDataAsync(Arg.Any<CancellationToken>())
            .Returns(_ => Task.FromResult(queued.Count > 1 ? queued.Dequeue() : queued.Peek()));
    }

    private static ProcessResourceData CreateProcess(
        string processName,
        long privateBytes,
        long workingSetBytes,
        long cpuTimeTicks)
    {
        return new ProcessResourceData
        {
            ProcessId = Math.Abs(processName.GetHashCode()),
            ProcessName = processName,
            ExecutablePath = $"{processName}.exe",
            PrivateBytes = privateBytes,
            WorkingSetBytes = workingSetBytes,
            CpuTimeTicks = cpuTimeTicks
        };
    }

    private static long CpuTicksForPercent(double cpuPercent)
    {
        return (long)(cpuPercent / 100 * SnapshotInterval.Ticks * Environment.ProcessorCount);
    }

    private sealed class ListLogger<T> : ILogger<T>
    {
        public List<LogEntry> Entries { get; } = [];

        public IDisposable BeginScope<TState>(TState state) where TState : notnull => NullScope.Instance;

        public bool IsEnabled(LogLevel logLevel) => true;

        public void Log<TState>(
            LogLevel logLevel,
            EventId eventId,
            TState state,
            Exception? exception,
            Func<TState, Exception?, string> formatter)
        {
            Entries.Add(new LogEntry(logLevel, formatter(state, exception)));
        }
    }

    private sealed record LogEntry(LogLevel Level, string Message);

    private sealed class NullScope : IDisposable
    {
        public static readonly NullScope Instance = new();

        public void Dispose()
        {
        }
    }
}
