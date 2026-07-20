using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Time.Testing;
using WireBound.Avalonia.ViewModels;
using WireBound.Core;
using WireBound.Core.Helpers;
using WireBound.Core.Models;
using WireBound.Core.Services;
using WireBound.Tests.Fixtures;

namespace WireBound.Tests.ViewModels;

/// <summary>
/// Tests the page-scoped live process monitor. The process sampler is deliberately
/// pull-based, so the important contract here is that the Apps route is the only
/// owner that requests snapshots.
/// </summary>
public class AppsViewModelTests : IAsyncDisposable
{
    private const long Kibibyte = 1024;
    private const long Mebibyte = 1024 * Kibibyte;

    private readonly IUiDispatcher _dispatcher = new SynchronousDispatcher();
    private readonly FakeTimeProvider _timeProvider = new();
    private readonly IProcessUsageService _processUsageService;
    private readonly INavigationService _navigationService;
    private readonly ILogger<AppsViewModel> _logger;
    private readonly List<AppsViewModel> _createdViewModels = [];
    private string _currentRoute = Routes.Overview;

    public AppsViewModelTests()
    {
        _processUsageService = Substitute.For<IProcessUsageService>();
        _navigationService = Substitute.For<INavigationService>();
        _logger = Substitute.For<ILogger<AppsViewModel>>();

        _navigationService.CurrentView.Returns(_ => _currentRoute);
        ConfigureCapture();
    }

    [Test]
    public async Task Constructor_WhenRouteIsNotApps_DoesNotCaptureOrStartTimer()
    {
        // Arrange
        ConfigureCapture(CreateSnapshot(processId: 101, processName: "code"));

        // Act
        var viewModel = CreateViewModel();
        await Task.Yield();

        // Assert
        viewModel.IsPageActive.Should().BeFalse();
        viewModel.ProcessItems.Should().BeEmpty();
        await _processUsageService.DidNotReceive().CaptureAsync(Arg.Any<CancellationToken>());

        // Advancing a fake clock makes this a deterministic assertion that the
        // inactive page did not leave a background timer behind.
        _timeProvider.Advance(TimeSpan.FromMinutes(1));
        await Task.Yield();
        await _processUsageService.DidNotReceive().CaptureAsync(Arg.Any<CancellationToken>());
    }

    [Test]
    public async Task Constructor_WhenAppsRouteIsCurrent_CapturesInitialSnapshot()
    {
        // Arrange
        _currentRoute = Routes.Apps;
        var captureStarted = ConfigureCaptureWithSignal(
            CreateSnapshot(processId: 101, processName: "code", cpuPercent: 8.5, hasCpuSample: true));

        // Act
        var viewModel = CreateViewModel();
        await captureStarted.Task.WaitAsync(TimeSpan.FromSeconds(1));
        await Task.Yield();

        // Assert
        viewModel.IsPageActive.Should().BeTrue();
        viewModel.IsLoading.Should().BeFalse();
        viewModel.ProcessCount.Should().Be(1);
        viewModel.ProcessItems.Should().ContainSingle();
        await _processUsageService.Received(1).CaptureAsync(Arg.Any<CancellationToken>());
    }

    [Test]
    public async Task NavigationAway_CancelsInFlightCapture_AndStopsPeriodicCapture()
    {
        // Arrange
        _currentRoute = Routes.Apps;
        var captureStarted = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var captureCancelled = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var pendingCapture = new TaskCompletionSource<IReadOnlyList<ProcessUsageSnapshot>>(
            TaskCreationOptions.RunContinuationsAsynchronously);

        _processUsageService.CaptureAsync(Arg.Any<CancellationToken>())
            .Returns(callInfo =>
            {
                var token = callInfo.Arg<CancellationToken>();
                token.Register(() =>
                {
                    captureCancelled.TrySetResult();
                    pendingCapture.TrySetCanceled(token);
                });
                captureStarted.TrySetResult();
                return pendingCapture.Task;
            });

        var viewModel = CreateViewModel();
        await captureStarted.Task.WaitAsync(TimeSpan.FromSeconds(1));

        // Act
        NavigateTo(Routes.Overview);
        await captureCancelled.Task.WaitAsync(TimeSpan.FromSeconds(1));
        await Task.Yield();

        // Assert
        viewModel.IsPageActive.Should().BeFalse();

        _processUsageService.ClearReceivedCalls();
        _timeProvider.Advance(TimeSpan.FromMinutes(1));
        await Task.Yield();
        await _processUsageService.DidNotReceive().CaptureAsync(Arg.Any<CancellationToken>());
    }

    [Test]
    public async Task NavigationBackToApps_ResumesWithOneFreshCapture()
    {
        // Arrange
        _currentRoute = Routes.Apps;
        var captureCount = 0;
        var secondCaptureStarted = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        _processUsageService.CaptureAsync(Arg.Any<CancellationToken>())
            .Returns(_ =>
            {
                captureCount++;
                if (captureCount == 2)
                {
                    secondCaptureStarted.TrySetResult();
                }

                IReadOnlyList<ProcessUsageSnapshot> result =
                [
                    CreateSnapshot(
                        processId: captureCount == 1 ? 101 : 202,
                        processName: captureCount == 1 ? "first" : "resumed")
                ];
                return Task.FromResult(result);
            });

        var viewModel = CreateViewModel();
        await WaitUntilAsync(() => captureCount == 1);

        // Act
        NavigateTo(Routes.Overview);
        NavigateTo(Routes.Apps);
        await secondCaptureStarted.Task.WaitAsync(TimeSpan.FromSeconds(1));
        await Task.Yield();

        // Assert
        captureCount.Should().Be(2);
        viewModel.IsPageActive.Should().BeTrue();
        viewModel.ProcessItems.Should().ContainSingle();
        viewModel.ProcessItems[0].ProcessId.Should().Be(202);
    }

    [Test]
    public async Task Capture_MapsProcessSnapshotIntoDisplayAndSummaryMetrics()
    {
        // Arrange
        _currentRoute = Routes.Apps;
        var chromeMemory = 512 * Mebibyte;
        var workerMemory = 64 * Mebibyte;
        var chromeDownload = 2 * Mebibyte;
        var chromeUpload = 128 * Kibibyte;
        ConfigureCapture(
            CreateSnapshot(
                processId: 101,
                processName: "chrome",
                executablePath: "/opt/chrome/chrome",
                privateBytes: chromeMemory,
                workingSetBytes: chromeMemory,
                cpuPercent: 12.5,
                hasCpuSample: true,
                downloadSpeedBps: chromeDownload,
                uploadSpeedBps: chromeUpload,
                sessionBytesReceived: 9 * Mebibyte,
                sessionBytesSent: 3 * Mebibyte,
                hasNetworkStats: true),
            CreateSnapshot(
                processId: 102,
                processName: "worker",
                executablePath: "/opt/worker/worker",
                privateBytes: workerMemory,
                workingSetBytes: workerMemory,
                cpuPercent: 2.5,
                hasCpuSample: true));

        var viewModel = CreateViewModel();
        await WaitUntilAsync(() => viewModel.ProcessItems.Count == 2);

        // Assert
        var chrome = viewModel.ProcessItems.Single(item => item.ProcessId == 101);
        chrome.DisplayName.Should().Be("chrome");
        chrome.ProcessName.Should().Be("chrome");
        chrome.ExecutablePath.Should().Be("/opt/chrome/chrome");
        chrome.CpuDisplay.Should().Be("12.5%");
        chrome.MemoryDisplay.Should().Be(ByteFormatter.FormatBytes(chromeMemory));
        chrome.DownloadDisplay.Should().Be(ByteFormatter.FormatSpeed(chromeDownload));
        chrome.UploadDisplay.Should().Be(ByteFormatter.FormatSpeed(chromeUpload));
        chrome.SessionTotalDisplay.Should().Be(ByteFormatter.FormatBytes(12 * Mebibyte));
        chrome.HasCpuSample.Should().BeTrue();
        chrome.HasNetworkStats.Should().BeTrue();

        viewModel.ProcessCount.Should().Be(2);
        viewModel.TotalCpu.Should().Be("15.0%");
        viewModel.TotalMemory.Should().Be(ByteFormatter.FormatBytes(chromeMemory + workerMemory));
        viewModel.TotalDownloadSpeed.Should().Be(ByteFormatter.FormatSpeed(chromeDownload));
        viewModel.TotalUploadSpeed.Should().Be(ByteFormatter.FormatSpeed(chromeUpload));
    }

    [Test]
    public async Task Filters_CanHideSystemProcesses_AndApplySearchLocally()
    {
        // Arrange
        _currentRoute = Routes.Apps;
        ConfigureCapture(
            CreateSnapshot(processId: 4, processName: "System", executablePath: string.Empty),
            CreateSnapshot(processId: 101, processName: "chrome", executablePath: "/opt/chrome/chrome"),
            CreateSnapshot(processId: 202, processName: "code", executablePath: "/opt/code/code"));

        var viewModel = CreateViewModel();
        await WaitUntilAsync(() => viewModel.ProcessCount == 3);

        // Assert - the total tracks the full snapshot and system processes can
        // be included for a complete Task Manager-style view.
        viewModel.ShowSystemProcesses.Should().BeTrue();
        viewModel.ProcessItems.Select(item => item.ProcessId).Should().BeEquivalentTo(new[] { 4, 101, 202 });

        // Act
        viewModel.ShowSystemProcesses = false;

        // Assert
        viewModel.ProcessItems.Select(item => item.ProcessId).Should().BeEquivalentTo(new[] { 101, 202 });

        // Act
        viewModel.SearchText = "202";

        // Assert
        viewModel.ProcessItems.Should().ContainSingle();
        viewModel.ProcessItems[0].ProcessName.Should().Be("code");
        await _processUsageService.Received(1).CaptureAsync(Arg.Any<CancellationToken>());
    }

    [Test]
    public async Task ToggleSortCommand_SortsByNameThenNetworkDownload()
    {
        // Arrange
        _currentRoute = Routes.Apps;
        ConfigureCapture(
            CreateSnapshot(processId: 1, processName: "zulu", executablePath: "/opt/zulu/zulu", downloadSpeedBps: 1 * Mebibyte),
            CreateSnapshot(processId: 2, processName: "alpha", executablePath: "/opt/alpha/alpha", downloadSpeedBps: 3 * Mebibyte),
            CreateSnapshot(processId: 3, processName: "bravo", executablePath: "/opt/bravo/bravo", downloadSpeedBps: 2 * Mebibyte));

        var viewModel = CreateViewModel();
        await WaitUntilAsync(() => viewModel.ProcessItems.Count == 3);

        // Act
        viewModel.ToggleSortCommand.Execute(ProcessUsageSortColumn.Name);

        // Assert
        viewModel.SortColumn.Should().Be(ProcessUsageSortColumn.Name);
        viewModel.SortDescending.Should().BeFalse();
        viewModel.ProcessItems.Select(item => item.ProcessName)
            .Should().Equal("alpha", "bravo", "zulu");

        // Act
        viewModel.ToggleSortCommand.Execute(ProcessUsageSortColumn.Download);

        // Assert
        viewModel.SortColumn.Should().Be(ProcessUsageSortColumn.Download);
        viewModel.SortDescending.Should().BeTrue();
        viewModel.ProcessItems.Select(item => item.ProcessName)
            .Should().Equal("alpha", "bravo", "zulu");
    }

    [Test]
    public async Task RefreshCommand_WhenPageActive_ReplacesTheSnapshot()
    {
        // Arrange
        _currentRoute = Routes.Apps;
        var snapshots = new Queue<IReadOnlyList<ProcessUsageSnapshot>>(
        [
            [CreateSnapshot(processId: 101, processName: "before")],
            [CreateSnapshot(processId: 202, processName: "after")]
        ]);
        _processUsageService.CaptureAsync(Arg.Any<CancellationToken>())
            .Returns(_ => Task.FromResult(snapshots.Dequeue()));

        var viewModel = CreateViewModel();
        await WaitUntilAsync(() => viewModel.ProcessItems.SingleOrDefault()?.ProcessId == 101);
        _processUsageService.ClearReceivedCalls();

        // Act
        await viewModel.RefreshCommand.ExecuteAsync(null);

        // Assert
        viewModel.ProcessItems.Should().ContainSingle();
        viewModel.ProcessItems[0].ProcessId.Should().Be(202);
        await _processUsageService.Received(1).CaptureAsync(Arg.Any<CancellationToken>());
    }

    [Test]
    public async Task Refresh_WithUnchangedVisibleOrder_UpdatesRowsWithoutResettingTheVirtualizedCollection()
    {
        // Arrange
        _currentRoute = Routes.Apps;
        var snapshots = new Queue<IReadOnlyList<ProcessUsageSnapshot>>(
        [
            [
                CreateSnapshot(processId: 101, processName: "alpha", cpuPercent: 5, hasCpuSample: true),
                CreateSnapshot(processId: 202, processName: "bravo", cpuPercent: 2, hasCpuSample: true)
            ],
            [
                CreateSnapshot(processId: 101, processName: "alpha", cpuPercent: 8, hasCpuSample: true),
                CreateSnapshot(processId: 202, processName: "bravo", cpuPercent: 3, hasCpuSample: true)
            ]
        ]);
        _processUsageService.CaptureAsync(Arg.Any<CancellationToken>())
            .Returns(_ => Task.FromResult(snapshots.Dequeue()));

        var viewModel = CreateViewModel();
        await WaitUntilAsync(() => viewModel.ProcessItems.Count == 2);
        var alpha = viewModel.ProcessItems.Single(item => item.ProcessId == 101);
        var collectionResets = 0;
        viewModel.ProcessItems.CollectionChanged += (_, eventArgs) =>
        {
            if (eventArgs.Action == System.Collections.Specialized.NotifyCollectionChangedAction.Reset)
            {
                collectionResets++;
            }
        };

        // Act
        await viewModel.RefreshCommand.ExecuteAsync(null);

        // Assert
        collectionResets.Should().Be(0);
        viewModel.ProcessItems.Single(item => item.ProcessId == 101).Should().BeSameAs(alpha);
        alpha.CpuDisplay.Should().Be("8.0%");
    }

    [Test]
    public async Task CaptureFailure_ClearsLoadingStateAndLeavesRefreshAvailable()
    {
        // Arrange
        _currentRoute = Routes.Apps;
        _processUsageService.CaptureAsync(Arg.Any<CancellationToken>())
            .Returns(Task.FromException<IReadOnlyList<ProcessUsageSnapshot>>(new InvalidOperationException("provider unavailable")));

        // Act
        var viewModel = CreateViewModel();
        await WaitUntilAsync(() => !viewModel.IsLoading);

        // Assert
        viewModel.CaptureError.Should().Be("Couldn’t refresh process usage. Try again.");
        viewModel.LastUpdatedLabel.Should().Be(viewModel.CaptureError);
        viewModel.RefreshCommand.CanExecute(null).Should().BeTrue();
    }

    [Test]
    public async Task Dispose_UnsubscribesFromNavigation_AndCannotRestartCapture()
    {
        // Arrange
        ConfigureCapture(CreateSnapshot(processId: 101, processName: "code"));
        var viewModel = CreateViewModel();

        // Act
        viewModel.Dispose();
        NavigateTo(Routes.Apps);
        _timeProvider.Advance(TimeSpan.FromMinutes(1));
        await Task.Yield();

        // Assert
        viewModel.IsPageActive.Should().BeFalse();
        await _processUsageService.DidNotReceive().CaptureAsync(Arg.Any<CancellationToken>());
    }

    public ValueTask DisposeAsync()
    {
        foreach (var viewModel in _createdViewModels)
        {
            viewModel.Dispose();
        }

        _createdViewModels.Clear();
        return ValueTask.CompletedTask;
    }

    private AppsViewModel CreateViewModel()
    {
        var viewModel = new AppsViewModel(
            _dispatcher,
            _processUsageService,
            _navigationService,
            logger: _logger,
            timeProvider: _timeProvider);
        _createdViewModels.Add(viewModel);
        return viewModel;
    }

    private void NavigateTo(string route)
    {
        _currentRoute = route;
        _navigationService.NavigationChanged += Raise.Event<Action<string>>(route);
    }

    private void ConfigureCapture(params ProcessUsageSnapshot[] snapshots)
    {
        _processUsageService.CaptureAsync(Arg.Any<CancellationToken>())
            .Returns(Task.FromResult<IReadOnlyList<ProcessUsageSnapshot>>(snapshots));
    }

    private TaskCompletionSource ConfigureCaptureWithSignal(params ProcessUsageSnapshot[] snapshots)
    {
        var started = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        _processUsageService.CaptureAsync(Arg.Any<CancellationToken>())
            .Returns(_ =>
            {
                started.TrySetResult();
                return Task.FromResult<IReadOnlyList<ProcessUsageSnapshot>>(snapshots);
            });
        return started;
    }

    private static async Task WaitUntilAsync(Func<bool> condition)
    {
        for (var attempt = 0; attempt < 20; attempt++)
        {
            if (condition())
            {
                return;
            }

            await Task.Yield();
        }

        condition().Should().BeTrue("the asynchronous process snapshot should have completed");
    }

    private static ProcessUsageSnapshot CreateSnapshot(
        int processId,
        string processName,
        string executablePath = "/opt/app/app",
        long privateBytes = 0,
        long? workingSetBytes = null,
        double cpuPercent = 0,
        bool hasCpuSample = false,
        long downloadSpeedBps = 0,
        long uploadSpeedBps = 0,
        long sessionBytesReceived = 0,
        long sessionBytesSent = 0,
        bool hasNetworkStats = false)
    {
        return new ProcessUsageSnapshot
        {
            ProcessId = processId,
            ProcessName = processName,
            ExecutablePath = executablePath,
            PrivateBytes = privateBytes,
            WorkingSetBytes = workingSetBytes ?? privateBytes,
            CpuPercent = cpuPercent,
            HasCpuSample = hasCpuSample,
            DownloadSpeedBps = downloadSpeedBps,
            UploadSpeedBps = uploadSpeedBps,
            SessionBytesReceived = sessionBytesReceived,
            SessionBytesSent = sessionBytesSent,
            HasNetworkStats = hasNetworkStats
        };
    }
}
