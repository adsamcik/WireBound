using Microsoft.Extensions.Time.Testing;
using WireBound.Avalonia.ViewModels;
using WireBound.Core;
using WireBound.Core.Models;
using WireBound.Core.Services;
using WireBound.Tests.Fixtures;

namespace WireBound.Tests.ViewModels;

public sealed class DashboardViewModelTests : IAsyncDisposable
{
    private readonly IUiDispatcher _dispatcher = new SynchronousDispatcher();
    private readonly INetworkMonitorService _networkMonitor = Substitute.For<INetworkMonitorService>();
    private readonly ISystemMonitorService _systemMonitor = Substitute.For<ISystemMonitorService>();
    private readonly INavigationService _navigation = Substitute.For<INavigationService>();
    private readonly IDataPersistenceService _persistence = Substitute.For<IDataPersistenceService>();
    private readonly ISystemSnapshotRepository _systemSnapshots = Substitute.For<ISystemSnapshotRepository>();
    private readonly IProcessUsageService _processUsage = Substitute.For<IProcessUsageService>();
    private readonly FakeTimeProvider _timeProvider = new();

    private OverviewViewModel? _network;
    private SystemViewModel? _system;
    private AppsViewModel? _processes;
    private DashboardViewModel? _dashboard;

    public DashboardViewModelTests()
    {
        _navigation.CurrentView.Returns(Routes.Overview);
        _networkMonitor.GetAdapters(Arg.Any<bool>()).Returns([]);
        _networkMonitor.GetAllAdapterStats().Returns(new Dictionary<string, NetworkStats>());
        _networkMonitor.GetPrimaryAdapterId().Returns(string.Empty);
        _networkMonitor.GetCurrentStats().Returns(new NetworkStats { Timestamp = DateTime.Now });
        _systemMonitor.GetCurrentStats().Returns(CreateSystemStats(cpu: 25, memory: 50, disk: 10));
        _systemMonitor.GetProcessorName().Returns("Test CPU");
        _systemMonitor.GetProcessorCount().Returns(8);
        _persistence.GetTodayUsageAsync().Returns((0L, 0L));
        _persistence.GetSettingsAsync().Returns(new AppSettings());
        _systemSnapshots.GetSystemHistoryAsync(Arg.Any<DateTime>()).Returns([]);
        _processUsage.CaptureAsync(Arg.Any<CancellationToken>())
            .Returns(Task.FromResult<IReadOnlyList<ProcessUsageSnapshot>>([]));
    }

    [Test]
    public void SelectResource_ChangesFocusSignalAndContributorSort()
    {
        var dashboard = CreateDashboard();

        dashboard.SelectResourceCommand.Execute(DashboardResource.Memory);

        dashboard.IsAutomaticFocus.Should().BeFalse();
        dashboard.IsMemoryFocus.Should().BeTrue();
        dashboard.FocusTitle.Should().Be("Memory activity");
        dashboard.SignalOptions.Should().Equal("Used memory");
        dashboard.Processes.SortColumn.Should().Be(ProcessUsageSortColumn.Memory);
        dashboard.Processes.SortDescending.Should().BeTrue();
    }

    [Test]
    public void AutomaticFocus_SelectsResourceWithActionablePressure()
    {
        _systemMonitor.GetCurrentStats().Returns(CreateSystemStats(cpu: 30, memory: 91, disk: 20));
        var dashboard = CreateDashboard();

        dashboard.EnableAutomaticFocusCommand.Execute(null);

        dashboard.IsAutomaticFocus.Should().BeTrue();
        dashboard.SelectedResource.Should().Be(DashboardResource.Memory);
    }

    [Test]
    public void TimeAndScopeFilters_UpdateUnderlyingMonitors()
    {
        var dashboard = CreateDashboard();

        dashboard.SelectedTimeRangeOption = DashboardViewModel.TimeRangeOptions.Single(option => option.Value == TimeRange.FiveMinutes);
        dashboard.SelectedProcessScope = "User processes";

        dashboard.Network.SelectedTimeRange.Should().Be(TimeRange.FiveMinutes);
        dashboard.Processes.ShowSystemProcesses.Should().BeFalse();
    }

    [Test]
    public void OpenDetails_UsesResourceSpecificLegacyDrillDown()
    {
        var dashboard = CreateDashboard();
        _navigation.ClearReceivedCalls();

        dashboard.OpenResourceDetailsCommand.Execute(null);
        dashboard.SelectResourceCommand.Execute(DashboardResource.Cpu);
        dashboard.OpenResourceDetailsCommand.Execute(null);

        _navigation.Received(1).NavigateTo(Routes.Charts);
        _navigation.Received(1).NavigateTo(Routes.System);
    }

    private DashboardViewModel CreateDashboard()
    {
        _network = new OverviewViewModel(
            _dispatcher,
            _networkMonitor,
            _systemMonitor,
            _navigation,
            _persistence);
        _system = new SystemViewModel(
            _dispatcher,
            _systemMonitor,
            _navigation,
            _systemSnapshots);
        _processes = new AppsViewModel(
            _dispatcher,
            _processUsage,
            _navigation,
            timeProvider: _timeProvider);
        _dashboard = new DashboardViewModel(_network, _system, _processes, _navigation);
        return _dashboard;
    }

    private static SystemStats CreateSystemStats(double cpu, double memory, double disk)
    {
        const long totalMemory = 1000;
        return new SystemStats
        {
            Timestamp = DateTime.Now,
            Cpu = new CpuStats { UsagePercent = cpu, PerCoreUsagePercent = [] },
            Memory = new MemoryStats
            {
                TotalBytes = totalMemory,
                UsedBytes = (long)(totalMemory * memory / 100),
                AvailableBytes = (long)(totalMemory * (100 - memory) / 100)
            },
            Disk = new DiskStats { ActivityPercent = disk }
        };
    }

    public ValueTask DisposeAsync()
    {
        _dashboard?.Dispose();
        _processes?.Dispose();
        _system?.Dispose();
        _network?.Dispose();
        return ValueTask.CompletedTask;
    }
}
