using AwesomeAssertions;
using WireBound.Avalonia.ViewModels;
using WireBound.Core;
using WireBound.Core.Models;
using WireBound.Core.Services;
using WireBound.Tests.Fixtures;

namespace WireBound.Tests.ViewModels;

/// <summary>
/// Unit tests for SystemViewModel
/// </summary>
public class SystemViewModelTests : IAsyncDisposable
{
    private readonly List<SystemViewModel> _createdViewModels = [];
    private readonly IUiDispatcher _dispatcherMock;
    private readonly ISystemMonitorService _systemMonitorMock;
    private readonly INavigationService _navigationServiceMock;
    private readonly ISystemSnapshotRepository _systemSnapshotRepositoryMock;
    private readonly ISystemHistoryService _systemHistoryMock;
    private readonly IDataPersistenceService _persistenceMock;

    public SystemViewModelTests()
    {
        _dispatcherMock = Substitute.For<IUiDispatcher>();
        _systemMonitorMock = Substitute.For<ISystemMonitorService>();
        _navigationServiceMock = Substitute.For<INavigationService>();
        _systemSnapshotRepositoryMock = Substitute.For<ISystemSnapshotRepository>();
        _systemHistoryMock = Substitute.For<ISystemHistoryService>();
        _persistenceMock = Substitute.For<IDataPersistenceService>();
        SetupDefaultMocks();
    }

    private void SetupDefaultMocks()
    {
        _systemMonitorMock.GetCurrentStats().Returns(CreateDefaultSystemStats());
        _systemMonitorMock.GetProcessorName().Returns("Test Processor");
        _systemMonitorMock.GetProcessorCount().Returns(8);
        _systemMonitorMock.IsCpuTemperatureAvailable.Returns(false);
        _navigationServiceMock.CurrentView.Returns(Routes.System);
        _systemSnapshotRepositoryMock.GetSystemHistoryAsync(Arg.Any<DateTime>()).Returns(new List<SystemSnapshot>());
        _systemHistoryMock.GetHourlyStatsAsync(Arg.Any<DateTime>(), Arg.Any<DateTime>()).Returns(new List<HourlySystemStats>());
        _persistenceMock.GetHourlyUsageAsync(Arg.Any<DateOnly>()).Returns(new List<HourlyUsage>());
        _dispatcherMock.InvokeAsync(Arg.Any<Action>()).Returns(call =>
        {
            call.Arg<Action>()();
            return Task.CompletedTask;
        });
        _dispatcherMock.When(x => x.Post(Arg.Any<Action>())).Do(call => call.Arg<Action>()());
        _dispatcherMock.When(x => x.Post(Arg.Any<Action>(), Arg.Any<UiDispatcherPriority>()))
            .Do(call => call.ArgAt<Action>(0)());
    }

    private static SystemStats CreateDefaultSystemStats()
    {
        return new SystemStats
        {
            Timestamp = DateTime.Now,
            Cpu = new CpuStats
            {
                UsagePercent = 25.5,
                PerCoreUsagePercent = [20.0, 30.0, 25.0, 27.0],
                ProcessorCount = 4,
                ProcessorName = "Test Processor",
                FrequencyMhz = 3600.0,
                TemperatureCelsius = null
            },
            Memory = new MemoryStats
            {
                TotalBytes = 16L * 1024 * 1024 * 1024, // 16 GB
                UsedBytes = 8L * 1024 * 1024 * 1024,   // 8 GB
                AvailableBytes = 8L * 1024 * 1024 * 1024 // 8 GB
            }
        };
    }

    private SystemViewModel CreateViewModel()
    {
        var viewModel = new SystemViewModel(
            _dispatcherMock,
            _systemMonitorMock,
            _navigationServiceMock,
            _systemSnapshotRepositoryMock,
            systemHistory: _systemHistoryMock,
            persistence: _persistenceMock);
        _createdViewModels.Add(viewModel);
        return viewModel;
    }

    #region Constructor Tests

    [Test]
    public void Constructor_InitializesProcessorInfo()
    {
        // Act
        var viewModel = CreateViewModel();

        // Assert
        viewModel.ProcessorName.Should().Be("Test Processor");
        viewModel.ProcessorCount.Should().Be(8);
    }

    [Test]
    public void Constructor_InitializesChartSeries()
    {
        // Act
        var viewModel = CreateViewModel();

        // Assert
        viewModel.CpuSeries.Should().NotBeNull();
        viewModel.CpuSeries.Should().HaveCount(1);
        viewModel.MemorySeries.Should().NotBeNull();
        viewModel.MemorySeries.Should().HaveCount(1);
    }

    [Test]
    public void Constructor_InitializesChartAxes()
    {
        // Act
        var viewModel = CreateViewModel();

        // Assert
        viewModel.CpuXAxes.Should().NotBeNull();
        viewModel.CpuYAxes.Should().NotBeNull();
        viewModel.MemoryXAxes.Should().NotBeNull();
        viewModel.MemoryYAxes.Should().NotBeNull();
    }

    [Test]
    public void Constructor_InitializesHistoryCollections()
    {
        // Act
        var viewModel = CreateViewModel();

        // Assert
        viewModel.CpuHistoryPoints.Should().NotBeNull();
        viewModel.MemoryHistoryPoints.Should().NotBeNull();
    }

    [Test]
    public void Constructor_LoadsInitialStats()
    {
        // Act
        var viewModel = CreateViewModel();

        // Assert
        viewModel.CpuUsagePercent.Should().Be(25.5);
        viewModel.CpuUsageFormatted.Should().Be("25.5%");
    }

    [Test]
    public void Constructor_ChecksCpuTemperatureAvailability()
    {
        // Arrange
        _systemMonitorMock.IsCpuTemperatureAvailable.Returns(true);

        // Act
        var viewModel = CreateViewModel();

        // Assert
        viewModel.IsCpuTemperatureAvailable.Should().BeTrue();
    }

    [Test]
    public void Constructor_WhenTemperatureNotAvailable_SetsIsCpuTemperatureAvailableFalse()
    {
        // Arrange
        _systemMonitorMock.IsCpuTemperatureAvailable.Returns(false);

        // Act
        var viewModel = CreateViewModel();

        // Assert
        viewModel.IsCpuTemperatureAvailable.Should().BeFalse();
    }

    #endregion

    #region Property Default Tests

    [Test]
    public void InitialState_HasDefaultFormattedValues()
    {
        // Act
        var viewModel = CreateViewModel();

        // Assert
        viewModel.MemoryUsed.Should().Be("8.00 GB");
        viewModel.MemoryTotal.Should().Be("16.00 GB");
        viewModel.MemoryAvailable.Should().Be("8.00 GB");
    }

    [Test]
    public void InitialState_PerCoreUsageIsPopulated()
    {
        // Act
        var viewModel = CreateViewModel();

        // Assert
        viewModel.PerCoreUsage.Should().NotBeNull();
        viewModel.PerCoreUsage.Should().HaveCount(4);
    }

    [Test]
    public void InitialState_CpuFrequencyIsSet()
    {
        // Act
        var viewModel = CreateViewModel();

        // Assert
        viewModel.CpuFrequencyMhz.Should().Be(3600.0);
    }

    #endregion

    #region Historical System Analysis Tests

    [Test]
    public async Task SystemViewModel_LoadsSystemTrendsForSelectedPeriod()
    {
        // Arrange
        var baseHour = DateTime.Today.AddHours(9);
        var systemData = new List<HourlySystemStats>
        {
            new() { Hour = baseHour, AvgCpuPercent = 40, MaxCpuPercent = 60, AvgMemoryPercent = 50, MaxMemoryPercent = 70 },
            new() { Hour = baseHour.AddHours(1), AvgCpuPercent = 60, MaxCpuPercent = 80, AvgMemoryPercent = 70, MaxMemoryPercent = 90 }
        };
        _systemHistoryMock.GetHourlyStatsAsync(Arg.Any<DateTime>(), Arg.Any<DateTime>()).Returns(systemData);

        var viewModel = CreateViewModel();
        await viewModel.InitializationTask;

        // Act
        viewModel.SelectSystemTrendsTabCommand.Execute(null);
        await viewModel.PendingLoadTask!;

        // Assert
        viewModel.SelectedSystemTab.Should().Be(SystemTab.SystemTrends);
        viewModel.AvgCpuPercent.Should().Be(50);
        viewModel.MaxCpuPercent.Should().Be(80);
        viewModel.AvgMemoryPercent.Should().Be(60);
        viewModel.MaxMemoryPercent.Should().Be(90);
        viewModel.SystemTrendChart.Should().NotBeEmpty();
        viewModel.HasData.Should().BeTrue();
    }

    [Test]
    public async Task SystemViewModel_LoadsCorrelationsForSelectedPeriod()
    {
        // Arrange
        var baseHour = DateTime.Today.AddHours(9);
        var systemData = new List<HourlySystemStats>
        {
            new() { Hour = baseHour, AvgCpuPercent = 20, AvgMemoryPercent = 30 },
            new() { Hour = baseHour.AddHours(1), AvgCpuPercent = 40, AvgMemoryPercent = 50 },
            new() { Hour = baseHour.AddHours(2), AvgCpuPercent = 60, AvgMemoryPercent = 70 }
        };
        var networkData = new List<HourlyUsage>
        {
            new() { Hour = baseHour, BytesReceived = 100, BytesSent = 20 },
            new() { Hour = baseHour.AddHours(1), BytesReceived = 200, BytesSent = 40 },
            new() { Hour = baseHour.AddHours(2), BytesReceived = 300, BytesSent = 60 }
        };
        _systemHistoryMock.GetHourlyStatsAsync(Arg.Any<DateTime>(), Arg.Any<DateTime>()).Returns(systemData);
        _persistenceMock.GetHourlyUsageAsync(Arg.Any<DateOnly>()).Returns(networkData);

        var viewModel = CreateViewModel();
        await viewModel.InitializationTask;

        // Act
        viewModel.SelectCorrelationsTabCommand.Execute(null);
        await viewModel.PendingLoadTask!;

        // Assert
        viewModel.SelectedSystemTab.Should().Be(SystemTab.Correlations);
        viewModel.NetworkCpuCorrelation.Should().BeApproximately(1, 0.0001);
        viewModel.NetworkMemoryCorrelation.Should().BeApproximately(1, 0.0001);
        viewModel.CpuMemoryCorrelation.Should().BeApproximately(1, 0.0001);
        viewModel.CorrelationInsights.Should().NotBeEmpty();
        viewModel.CorrelationChart.Should().NotBeEmpty();
        viewModel.HasData.Should().BeTrue();
    }

    [Test]
    public async Task SystemViewModel_PeriodChange_RefreshesData()
    {
        // Arrange
        var viewModel = CreateViewModel();
        await viewModel.InitializationTask;
        viewModel.SelectSystemTrendsTabCommand.Execute(null);
        await viewModel.PendingLoadTask!;
        _systemHistoryMock.ClearReceivedCalls();

        // Act
        viewModel.SetPeriodCommand.Execute(InsightsPeriod.ThisMonth);
        await viewModel.PendingLoadTask!;

        // Assert
        await _systemHistoryMock.Received().GetHourlyStatsAsync(Arg.Any<DateTime>(), Arg.Any<DateTime>());
    }

    #endregion

    #region Dispose Tests

    [Test]
    public void Dispose_CanBeCalledMultipleTimes()
    {
        // Arrange
        var viewModel = CreateViewModel();

        // Act & Assert - should not throw
        viewModel.Dispose();
        viewModel.Dispose();
    }

    #endregion

    public ValueTask DisposeAsync()
    {
        foreach (var vm in _createdViewModels)
        {
            vm.Dispose();
        }
        _createdViewModels.Clear();
        return ValueTask.CompletedTask;
    }
}
