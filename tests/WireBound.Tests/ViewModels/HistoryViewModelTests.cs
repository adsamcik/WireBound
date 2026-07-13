using AwesomeAssertions;
using WireBound.Avalonia.ViewModels;
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
