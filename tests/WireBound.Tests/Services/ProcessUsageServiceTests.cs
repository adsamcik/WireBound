using Microsoft.Extensions.Time.Testing;
using WireBound.Avalonia.Services;
using WireBound.Core.Models;
using WireBound.Core.Services;
using WireBound.Platform.Abstract.Models;
using WireBound.Platform.Abstract.Services;

namespace WireBound.Tests.Services;

public class ProcessUsageServiceTests
{
    [Test]
    public async Task CaptureAsync_ReturnsRawPerPidRows()
    {
        var (service, provider, _) = CreateService();
        IReadOnlyList<ProcessResourceData> resources =
        [
            CreateProcess(41, "browser", "/opt/browser", 123, 456, 789),
            CreateProcess(42, "worker", "/opt/worker", 234, 567, 890)
        ];
        provider.GetProcessResourceDataAsync(Arg.Any<CancellationToken>())
            .Returns(_ => Task.FromResult(resources));

        var snapshots = await service.CaptureAsync();

        snapshots.Should().HaveCount(2);
        var browser = snapshots.Single(snapshot => snapshot.ProcessId == 41);
        browser.ProcessName.Should().Be("browser");
        browser.ExecutablePath.Should().Be("/opt/browser");
        browser.PrivateBytes.Should().Be(123);
        browser.WorkingSetBytes.Should().Be(456);
        browser.CpuTimeTicks.Should().Be(789);
        browser.CpuPercent.Should().Be(0);
        browser.HasCpuSample.Should().BeFalse();
        browser.HasNetworkStats.Should().BeFalse();
    }

    [Test]
    public async Task CaptureAsync_SecondSampleCalculatesCpuPerPid()
    {
        var (service, provider, time) = CreateService();
        IReadOnlyList<ProcessResourceData> resources =
        [CreateProcess(7, "busy", "/opt/busy", 10, 20, 0)];
        provider.GetProcessResourceDataAsync(Arg.Any<CancellationToken>())
            .Returns(_ => Task.FromResult(resources));

        await service.CaptureAsync();

        var interval = TimeSpan.FromSeconds(2);
        time.Advance(interval);
        resources =
        [CreateProcess(7, "busy", "/opt/busy", 10, 20, CpuTicksForPercent(25, interval))];

        var snapshots = await service.CaptureAsync();

        var process = snapshots.Single();
        process.HasCpuSample.Should().BeTrue();
        process.CpuPercent.Should().BeApproximately(25, 0.001);
    }

    [Test]
    public async Task CaptureAsync_CapsCpuAtTotalMachineCapacity()
    {
        var (service, provider, time) = CreateService();
        IReadOnlyList<ProcessResourceData> resources =
        [CreateProcess(7, "busy", "/opt/busy", 10, 20, 0)];
        provider.GetProcessResourceDataAsync(Arg.Any<CancellationToken>())
            .Returns(_ => Task.FromResult(resources));

        await service.CaptureAsync();

        var interval = TimeSpan.FromSeconds(1);
        time.Advance(interval);
        resources =
        [CreateProcess(7, "busy", "/opt/busy", 10, 20, CpuTicksForPercent(150, interval))];

        var snapshot = (await service.CaptureAsync()).Single();

        snapshot.HasCpuSample.Should().BeTrue();
        snapshot.CpuPercent.Should().Be(100);
    }

    [Test]
    public async Task Reset_ClearsCpuBaseline()
    {
        var (service, provider, time) = CreateService();
        IReadOnlyList<ProcessResourceData> resources =
        [CreateProcess(7, "busy", "/opt/busy", 10, 20, 0)];
        provider.GetProcessResourceDataAsync(Arg.Any<CancellationToken>())
            .Returns(_ => Task.FromResult(resources));

        await service.CaptureAsync();
        time.Advance(TimeSpan.FromSeconds(1));
        resources =
        [CreateProcess(7, "busy", "/opt/busy", 10, 20, CpuTicksForPercent(40, TimeSpan.FromSeconds(1)))];
        var measured = await service.CaptureAsync();
        measured.Single().HasCpuSample.Should().BeTrue();

        service.Reset();
        time.Advance(TimeSpan.FromSeconds(1));
        resources =
        [CreateProcess(7, "busy", "/opt/busy", 10, 20, CpuTicksForPercent(80, TimeSpan.FromSeconds(1)))];

        var afterReset = await service.CaptureAsync();

        afterReset.Single().HasCpuSample.Should().BeFalse();
        afterReset.Single().CpuPercent.Should().Be(0);
    }

    [Test]
    public async Task CaptureAsync_JoinsRunningNetworkStatsWithoutChangingOwnership()
    {
        var networkService = Substitute.For<IProcessNetworkService>();
        networkService.IsRunning.Returns(true);
        networkService.GetCurrentStats().Returns(
            new List<ProcessNetworkStats>
            {
                new()
                {
                    ProcessId = 31,
                    DownloadSpeedBps = 1_500,
                    UploadSpeedBps = 250,
                    SessionBytesReceived = 50_000,
                    SessionBytesSent = 5_000
                }
            });

        var (service, provider, _) = CreateService(networkService);
        IReadOnlyList<ProcessResourceData> resources =
        [CreateProcess(31, "downloader", "/opt/downloader", 10, 20, 0)];
        provider.GetProcessResourceDataAsync(Arg.Any<CancellationToken>())
            .Returns(_ => Task.FromResult(resources));

        var snapshot = (await service.CaptureAsync()).Single();

        snapshot.HasNetworkStats.Should().BeTrue();
        snapshot.DownloadSpeedBps.Should().Be(1_500);
        snapshot.UploadSpeedBps.Should().Be(250);
        snapshot.SessionBytesReceived.Should().Be(50_000);
        snapshot.SessionBytesSent.Should().Be(5_000);
        _ = networkService.DidNotReceive().StartAsync();
        _ = networkService.DidNotReceive().StopAsync();
    }

    [Test]
    public async Task CaptureAsync_IgnoresStaleNetworkDataWhenMonitoringIsStopped()
    {
        var networkService = Substitute.For<IProcessNetworkService>();
        networkService.IsRunning.Returns(false);
        networkService.GetCurrentStats().Returns(
            new List<ProcessNetworkStats>
            {
                new() { ProcessId = 31, DownloadSpeedBps = 1_500 }
            });

        var (service, provider, _) = CreateService(networkService);
        IReadOnlyList<ProcessResourceData> resources =
        [CreateProcess(31, "downloader", "/opt/downloader", 10, 20, 0)];
        provider.GetProcessResourceDataAsync(Arg.Any<CancellationToken>())
            .Returns(_ => Task.FromResult(resources));

        var snapshot = (await service.CaptureAsync()).Single();

        snapshot.HasNetworkStats.Should().BeFalse();
        snapshot.DownloadSpeedBps.Should().Be(0);
        networkService.DidNotReceive().GetCurrentStats();
    }

    [Test]
    public async Task CaptureAsync_SerializesConcurrentCaptures()
    {
        var (service, provider, _) = CreateService();
        var firstCaptureStarted = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var releaseFirstCapture = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var invocationCount = 0;

        provider.GetProcessResourceDataAsync(Arg.Any<CancellationToken>())
            .Returns(_ => CaptureResourcesAsync());

        var first = service.CaptureAsync();
        await firstCaptureStarted.Task.WaitAsync(TimeSpan.FromSeconds(1));

        var second = service.CaptureAsync();

        invocationCount.Should().Be(1);
        second.IsCompleted.Should().BeFalse();

        releaseFirstCapture.TrySetResult();
        await Task.WhenAll(first, second);

        invocationCount.Should().Be(2);

        async Task<IReadOnlyList<ProcessResourceData>> CaptureResourcesAsync()
        {
            var invocation = Interlocked.Increment(ref invocationCount);
            if (invocation == 1)
            {
                firstCaptureStarted.TrySetResult();
                await releaseFirstCapture.Task;
            }

            return [CreateProcess(invocation, "worker", "/opt/worker", 10, 20, invocation)];
        }
    }

    [Test]
    public async Task CaptureAsync_CanceledProviderCallDoesNotCommitABaseline()
    {
        var (service, provider, _) = CreateService();
        var providerCallStarted = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var pendingProviderResult = new TaskCompletionSource<IReadOnlyList<ProcessResourceData>>(
            TaskCreationOptions.RunContinuationsAsynchronously);

        provider.GetProcessResourceDataAsync(Arg.Any<CancellationToken>())
            .Returns(callInfo =>
            {
                providerCallStarted.TrySetResult();
                return pendingProviderResult.Task.WaitAsync(callInfo.Arg<CancellationToken>());
            });

        using var cancellation = new CancellationTokenSource();
        var canceledCapture = service.CaptureAsync(cancellation.Token);
        await providerCallStarted.Task.WaitAsync(TimeSpan.FromSeconds(1));
        cancellation.Cancel();

        Exception? cancellationException = null;
        try
        {
            await canceledCapture;
        }
        catch (OperationCanceledException ex)
        {
            cancellationException = ex;
        }

        cancellationException.Should().BeAssignableTo<OperationCanceledException>();

        IReadOnlyList<ProcessResourceData> validResources =
        [CreateProcess(9, "worker", "/opt/worker", 10, 20, TimeSpan.TicksPerSecond)];
        provider.GetProcessResourceDataAsync(Arg.Any<CancellationToken>())
            .Returns(_ => Task.FromResult(validResources));

        var nextCapture = (await service.CaptureAsync()).Single();

        nextCapture.HasCpuSample.Should().BeFalse();
        nextCapture.CpuPercent.Should().Be(0);
        pendingProviderResult.TrySetCanceled();
    }

    private static (ProcessUsageService Service, IProcessResourceProvider Provider, FakeTimeProvider Time) CreateService(
        IProcessNetworkService? processNetworkService = null)
    {
        var provider = Substitute.For<IProcessResourceProvider>();
        var time = new FakeTimeProvider();
        var service = new ProcessUsageService(provider, processNetworkService, time);
        return (service, provider, time);
    }

    private static ProcessResourceData CreateProcess(
        int processId,
        string processName,
        string executablePath,
        long privateBytes,
        long workingSetBytes,
        long cpuTimeTicks)
    {
        return new ProcessResourceData
        {
            ProcessId = processId,
            ProcessName = processName,
            ExecutablePath = executablePath,
            PrivateBytes = privateBytes,
            WorkingSetBytes = workingSetBytes,
            CpuTimeTicks = cpuTimeTicks
        };
    }

    private static long CpuTicksForPercent(double cpuPercent, TimeSpan interval)
    {
        return (long)(cpuPercent / 100 * interval.Ticks * Environment.ProcessorCount);
    }
}
