using AwesomeAssertions;
using WireBound.Avalonia.ViewModels;
using WireBound.Core.Helpers;
using WireBound.Core.Models;
using WireBound.Core.Services;

namespace WireBound.Tests.ViewModels;

/// <summary>
/// Unit tests for <see cref="HistoryViewModel"/> — the unified historical dashboard.
/// </summary>
public class HistoryViewModelTests : IAsyncDisposable
{
    private readonly List<HistoryViewModel> _created = [];
    private readonly IUiDispatcher _dispatcherMock;
    private readonly INavigationService _navigationServiceMock;
    private readonly ISystemHistoryService _systemHistoryMock;
    private readonly INetworkUsageRepository _networkUsageMock;
    private readonly IAppOverviewService _appOverviewMock;

    public HistoryViewModelTests()
    {
        _dispatcherMock = Substitute.For<IUiDispatcher>();
        _navigationServiceMock = Substitute.For<INavigationService>();
        _systemHistoryMock = Substitute.For<ISystemHistoryService>();
        _networkUsageMock = Substitute.For<INetworkUsageRepository>();
        _appOverviewMock = Substitute.For<IAppOverviewService>();

        // Default empty results so construction's initial load doesn't NRE.
        _networkUsageMock.GetDailyUsageAsync(Arg.Any<DateOnly>(), Arg.Any<DateOnly>())
            .Returns(new List<DailyUsage>());
        _networkUsageMock.GetHourlyUsageAsync(Arg.Any<DateOnly>())
            .Returns(new List<HourlyUsage>());
        _systemHistoryMock.GetHourlyStatsAsync(Arg.Any<DateTime>(), Arg.Any<DateTime>())
            .Returns(new List<HourlySystemStats>());
        IReadOnlyList<AppOverview> emptyApps = new List<AppOverview>();
        _appOverviewMock.GetOverviewAsync(Arg.Any<DateOnly>(), Arg.Any<DateOnly>(), Arg.Any<CancellationToken>())
            .Returns(emptyApps);

        // Run dispatched UI updates synchronously so assertions see the result.
        _dispatcherMock.InvokeAsync(Arg.Any<Action>()).Returns(call =>
        {
            call.Arg<Action>()();
            return Task.CompletedTask;
        });
    }

    private HistoryViewModel CreateViewModel()
    {
        var vm = new HistoryViewModel(
            _dispatcherMock,
            _navigationServiceMock,
            _systemHistoryMock,
            _networkUsageMock,
            _appOverviewMock);
        _created.Add(vm);
        return vm;
    }

    private static AppOverview CreateApp(
        string id, string name, long received, long sent, long localReceived = 0, long localSent = 0) =>
        new(id, name, id, "/" + id, "", received, sent, 0, 0, 0, 0, 0, 0, DateTime.Now, DateTime.Now, 1)
        {
            LoopbackBytesReceived = localReceived,
            LoopbackBytesSent = localSent
        };

    [Test]
    public void Constructor_DefaultsToThisWeek()
    {
        var vm = CreateViewModel();

        vm.SelectedPeriod.Should().Be(InsightsPeriod.ThisWeek);
        vm.IsCustomPeriod.Should().BeFalse();
    }

    [Test]
    public async Task Load_WithData_PopulatesTotalsTrendsAndTopApps()
    {
        // Arrange
        _networkUsageMock.GetDailyUsageAsync(Arg.Any<DateOnly>(), Arg.Any<DateOnly>())
            .Returns(new List<DailyUsage>
            {
                new() { Date = DateOnly.FromDateTime(DateTime.Today), BytesReceived = 3_000, BytesSent = 1_000 },
                new() { Date = DateOnly.FromDateTime(DateTime.Today.AddDays(-1)), BytesReceived = 5_000, BytesSent = 2_000 }
            });

        var baseHour = DateTime.Today.AddHours(9);
        _systemHistoryMock.GetHourlyStatsAsync(Arg.Any<DateTime>(), Arg.Any<DateTime>())
            .Returns(new List<HourlySystemStats>
            {
                new() { Hour = baseHour, AvgCpuPercent = 40, AvgMemoryPercent = 50, AvgDiskActivityPercent = 10 },
                new() { Hour = baseHour.AddHours(1), AvgCpuPercent = 60, AvgMemoryPercent = 70, AvgDiskActivityPercent = 30 }
            });

        IReadOnlyList<AppOverview> apps = new List<AppOverview>
        {
            new("app.one", "App One", "one", "/one", "", 2_000, 500, 0, 0, 0, 0, 0, 0, DateTime.Now, DateTime.Now, 1),
            new("app.two", "App Two", "two", "/two", "", 9_000, 1_000, 0, 0, 0, 0, 0, 0, DateTime.Now, DateTime.Now, 1)
        };
        _appOverviewMock.GetOverviewAsync(Arg.Any<DateOnly>(), Arg.Any<DateOnly>(), Arg.Any<CancellationToken>())
            .Returns(apps);

        // Act
        var vm = CreateViewModel();
        await vm.InitializationTask;

        // Assert
        vm.HasData.Should().BeTrue();
        vm.HasError.Should().BeFalse();
        vm.TotalDownload.Should().NotBe("0 B");
        vm.TotalUpload.Should().NotBe("0 B");
        vm.AvgCpuPercent.Should().Be(50);
        vm.AvgMemoryPercent.Should().Be(60);
        vm.AvgDiskActivityPercent.Should().Be(20);
        vm.NetworkSeries.Should().NotBeEmpty();
        vm.SystemSeries.Should().NotBeEmpty();
        vm.TopApps.Should().HaveCount(2);
        // Highest-traffic app sorts first.
        vm.TopApps[0].Name.Should().Be("App Two");
    }

    [Test]
    public async Task Load_WithNoData_SetsEmptyState()
    {
        var vm = CreateViewModel();
        await vm.InitializationTask;

        vm.HasData.Should().BeFalse();
        vm.HasError.Should().BeFalse();
        vm.ShowEmptyState.Should().BeTrue();
    }

    [Test]
    public async Task LocalhostToggle_ExcludesByDefault_AndIncludesAndReranksWhenEnabled()
    {
        // App One leads on external network traffic. App Two leads only when its
        // much larger local service-to-service traffic is included.
        IReadOnlyList<AppOverview> apps =
        [
            CreateApp("app.one", "App One", 1_000, 200),
            CreateApp("app.two", "App Two", 5_100, 600, localReceived: 5_000, localSent: 500)
        ];
        _appOverviewMock.GetOverviewAsync(Arg.Any<DateOnly>(), Arg.Any<DateOnly>(), Arg.Any<CancellationToken>())
            .Returns(apps);

        var vm = CreateViewModel();
        await vm.InitializationTask;

        vm.IncludeLocalhostTraffic.Should().BeFalse();
        vm.TopApps.Select(app => app.Name).Should().Equal("App One", "App Two");
        vm.TopApps[0].TotalFormatted.Should().Be(ByteFormatter.FormatBytes(1_200));
        vm.TopApps[1].TotalFormatted.Should().Be(ByteFormatter.FormatBytes(200));
        vm.TopApps[1].LocalTrafficLabel.Should().EndWith("local excluded");

        _appOverviewMock.ClearReceivedCalls();
        vm.IncludeLocalhostTraffic = true;

        vm.TopApps.Select(app => app.Name).Should().Equal("App Two", "App One");
        vm.TopApps[0].TotalFormatted.Should().Be(ByteFormatter.FormatBytes(5_700));
        vm.TopApps[0].DownloadFormatted.Should().Be(ByteFormatter.FormatBytes(5_100));
        vm.TopApps[0].UploadFormatted.Should().Be(ByteFormatter.FormatBytes(600));
        vm.TopApps[0].LocalTrafficLabel.Should().EndWith("local included");

        vm.IncludeLocalhostTraffic = false;

        vm.TopApps.Select(app => app.Name).Should().Equal("App One", "App Two");
        vm.TopApps[1].TotalFormatted.Should().Be(ByteFormatter.FormatBytes(200));
        vm.TopApps[1].DownloadFormatted.Should().Be(ByteFormatter.FormatBytes(100));
        vm.TopApps[1].UploadFormatted.Should().Be(ByteFormatter.FormatBytes(100));
        vm.TopApps[1].LocalTrafficLabel.Should().EndWith("local excluded");
        await _appOverviewMock.DidNotReceive()
            .GetOverviewAsync(Arg.Any<DateOnly>(), Arg.Any<DateOnly>(), Arg.Any<CancellationToken>());
    }

    [Test]
    public async Task LocalhostToggle_HidesLocalOnlyAppsWhileExcluded()
    {
        IReadOnlyList<AppOverview> apps =
        [
            CreateApp("local.only", "Local Only", 4_000, 1_000, localReceived: 4_000, localSent: 1_000)
        ];
        _appOverviewMock.GetOverviewAsync(Arg.Any<DateOnly>(), Arg.Any<DateOnly>(), Arg.Any<CancellationToken>())
            .Returns(apps);

        var vm = CreateViewModel();
        await vm.InitializationTask;

        vm.TopApps.Should().BeEmpty();
        vm.HasData.Should().BeTrue();
        vm.HasOverviewData.Should().BeFalse();
        vm.HasTopApps.Should().BeFalse();
        vm.ShowEmptyState.Should().BeFalse();
        vm.TopAppsEmptyMessage.Should().Contain("localhost");

        vm.IncludeLocalhostTraffic = true;

        vm.TopApps.Should().ContainSingle();
        vm.TopApps[0].Name.Should().Be("Local Only");
        vm.HasData.Should().BeTrue();

        vm.IncludeLocalhostTraffic = false;
        vm.TopApps.Should().BeEmpty();
        vm.HasData.Should().BeTrue();
        vm.ShowEmptyState.Should().BeFalse();
    }

    [Test]
    public async Task LocalhostToggle_RanksAllLoadedAppsBeforeLimitingRows()
    {
        IReadOnlyList<AppOverview> apps = Enumerable.Range(1, 13)
            .Select(index => CreateApp($"app{index}", $"App {index}", 1_000, 0))
            .Append(CreateApp("local", "Local Service", 50_000, 0, localReceived: 50_000))
            .ToList();
        _appOverviewMock.GetOverviewAsync(Arg.Any<DateOnly>(), Arg.Any<DateOnly>(), Arg.Any<CancellationToken>())
            .Returns(apps);

        var vm = CreateViewModel();
        await vm.InitializationTask;

        vm.TopApps.Should().HaveCount(12);
        vm.TopApps.Should().NotContain(app => app.Name == "Local Service");

        vm.IncludeLocalhostTraffic = true;

        vm.TopApps.Should().HaveCount(12);
        vm.TopApps[0].Name.Should().Be("Local Service");
    }

    [Test]
    public async Task PendingLoad_ShowsLoadingInsteadOfEmptyState()
    {
        var pending = new TaskCompletionSource<IReadOnlyList<AppOverview>>(TaskCreationOptions.RunContinuationsAsynchronously);
        _appOverviewMock.GetOverviewAsync(Arg.Any<DateOnly>(), Arg.Any<DateOnly>(), Arg.Any<CancellationToken>())
            .Returns(pending.Task);
        var vm = CreateViewModel();

        try
        {
            vm.IsLoading.Should().BeTrue();
            vm.ShowLoadingOverlay.Should().BeTrue();
            vm.ShowEmptyState.Should().BeFalse();
        }
        finally
        {
            pending.TrySetResult([]);
            await vm.InitializationTask;
        }

        vm.IsLoading.Should().BeFalse();
        vm.ShowEmptyState.Should().BeTrue();
    }

    [Test]
    public async Task SupersededLoad_KeepsCurrentRequestLoadingAndRefreshesCachedApps()
    {
        IReadOnlyList<AppOverview> initialApps = [CreateApp("initial", "Initial", 1_000, 0)];
        _appOverviewMock.GetOverviewAsync(Arg.Any<DateOnly>(), Arg.Any<DateOnly>(), Arg.Any<CancellationToken>())
            .Returns(initialApps);
        var vm = CreateViewModel();
        await vm.InitializationTask;
        vm.IncludeLocalhostTraffic = true;

        var superseded = new TaskCompletionSource<IReadOnlyList<AppOverview>>(TaskCreationOptions.RunContinuationsAsynchronously);
        var current = new TaskCompletionSource<IReadOnlyList<AppOverview>>(TaskCreationOptions.RunContinuationsAsynchronously);
        _appOverviewMock.GetOverviewAsync(Arg.Any<DateOnly>(), Arg.Any<DateOnly>(), Arg.Any<CancellationToken>())
            .Returns(superseded.Task, current.Task);
        vm.SelectedPeriod = InsightsPeriod.Today;
        var supersededLoad = vm.InitializationTask;
        vm.SelectedPeriod = InsightsPeriod.ThisMonth;

        try
        {
            superseded.SetResult([CreateApp("old", "Old", 2_000, 0)]);
            await supersededLoad;

            vm.IsLoading.Should().BeTrue();
            vm.TopApps[0].Name.Should().Be("Initial");
        }
        finally
        {
            current.TrySetResult([CreateApp("current", "Current", 5_000, 0, localReceived: 5_000)]);
            await vm.InitializationTask;
        }

        vm.IsLoading.Should().BeFalse();
        vm.IncludeLocalhostTraffic.Should().BeTrue();
        vm.TopApps.Should().ContainSingle();
        vm.TopApps[0].Name.Should().Be("Current");
        vm.TopApps[0].TotalFormatted.Should().Be(ByteFormatter.FormatBytes(5_000));
    }

    [Test]
    public async Task SupersededQueuedUpdate_DoesNotReplaceNewerResults()
    {
        var queuedUpdate = new TaskCompletionSource<Action>(TaskCreationOptions.RunContinuationsAsynchronously);
        var releaseUpdate = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var queueNextUpdate = true;
        _dispatcherMock.InvokeAsync(Arg.Any<Action>()).Returns(call =>
        {
            var action = call.Arg<Action>();
            if (queueNextUpdate)
            {
                queueNextUpdate = false;
                queuedUpdate.SetResult(action);
                return releaseUpdate.Task;
            }

            action();
            return Task.CompletedTask;
        });
        IReadOnlyList<AppOverview> olderApps = [CreateApp("old", "Old", 1_000, 0)];
        IReadOnlyList<AppOverview> currentApps = [CreateApp("current", "Current", 2_000, 0)];
        _appOverviewMock.GetOverviewAsync(Arg.Any<DateOnly>(), Arg.Any<DateOnly>(), Arg.Any<CancellationToken>())
            .Returns(olderApps, currentApps);
        var vm = CreateViewModel();
        var olderLoad = vm.InitializationTask;

        try
        {
            var applyQueuedUpdate = await queuedUpdate.Task.WaitAsync(TimeSpan.FromSeconds(5));
            vm.SelectedPeriod = InsightsPeriod.Today;
            await vm.InitializationTask;
            applyQueuedUpdate();
        }
        finally
        {
            releaseUpdate.TrySetResult();
            await olderLoad;
        }

        vm.TopApps.Should().ContainSingle();
        vm.TopApps[0].Name.Should().Be("Current");
        vm.IsLoading.Should().BeFalse();
    }

    [Test]
    public async Task SetPeriod_ChangesSelectedPeriodAndReloads()
    {
        var vm = CreateViewModel();
        await vm.InitializationTask;

        vm.SetPeriodCommand.Execute(InsightsPeriod.ThisMonth);
        await vm.InitializationTask;

        vm.SelectedPeriod.Should().Be(InsightsPeriod.ThisMonth);
        await _networkUsageMock.Received().GetDailyUsageAsync(Arg.Any<DateOnly>(), Arg.Any<DateOnly>());
    }

    [Test]
    public async Task SelectingCustomPeriod_SetsIsCustomPeriod()
    {
        var vm = CreateViewModel();
        await vm.InitializationTask;

        vm.SetPeriodCommand.Execute(InsightsPeriod.Custom);

        vm.IsCustomPeriod.Should().BeTrue();
    }

    [Test]
    public async Task TodayPeriod_UsesHourlyNetworkUsage()
    {
        var vm = CreateViewModel();
        await vm.InitializationTask;

        vm.SetPeriodCommand.Execute(InsightsPeriod.Today);
        await vm.InitializationTask;

        vm.SelectedPeriod.Should().Be(InsightsPeriod.Today);
        // A single-day range must drill into hourly buckets, not daily.
        await _networkUsageMock.Received().GetHourlyUsageAsync(Arg.Any<DateOnly>());
    }

    public async ValueTask DisposeAsync()
    {
        foreach (var vm in _created)
        {
            vm.Dispose();
        }

        await Task.CompletedTask;
        GC.SuppressFinalize(this);
    }
}
