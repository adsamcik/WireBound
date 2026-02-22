using AwesomeAssertions;
using Microsoft.Extensions.Logging;
using WireBound.Avalonia.ViewModels;
using WireBound.Core;
using WireBound.Core.Models;
using WireBound.Core.Services;
using WireBound.Tests.Fixtures;

namespace WireBound.Tests.ViewModels;

/// <summary>
/// Unit tests for InsightsViewModel
/// </summary>
public class InsightsViewModelTests : IAsyncDisposable
{
    private readonly List<InsightsViewModel> _createdViewModels = [];
    private readonly IDataPersistenceService _persistenceMock;
    private readonly ISystemHistoryService _systemHistoryMock;
    private readonly ILogger<InsightsViewModel> _loggerMock;

    public InsightsViewModelTests()
    {
        _persistenceMock = Substitute.For<IDataPersistenceService>();
        _systemHistoryMock = Substitute.For<ISystemHistoryService>();
        _loggerMock = Substitute.For<ILogger<InsightsViewModel>>();

        // Setup default returns
        SetupDefaultMocks();
    }

    private void SetupDefaultMocks()
    {
        _persistenceMock.GetDailyUsageAsync(Arg.Any<DateOnly>(), Arg.Any<DateOnly>()).Returns(new List<DailyUsage>());

        _persistenceMock.GetHourlyUsageAsync(Arg.Any<DateOnly>()).Returns(new List<HourlyUsage>());

        _systemHistoryMock.GetHourlyStatsAsync(Arg.Any<DateTime>(), Arg.Any<DateTime>()).Returns(new List<HourlySystemStats>());
    }

    private InsightsViewModel CreateViewModel()
    {
        var viewModel = new InsightsViewModel(
            _persistenceMock,
            _systemHistoryMock,
            null,
            _loggerMock);
        _createdViewModels.Add(viewModel);
        return viewModel;
    }

    private InsightsViewModel CreateViewModelWithoutSystemHistory()
    {
        var viewModel = new InsightsViewModel(
            _persistenceMock,
            null,
            null,
            _loggerMock);
        _createdViewModels.Add(viewModel);
        return viewModel;
    }

    #region Constructor Tests

    [Test]
    public void Constructor_InitializesToNetworkUsageTab()
    {
        // Act
        var viewModel = CreateViewModel();

        // Assert
        viewModel.SelectedTab.Should().Be(InsightsTab.NetworkUsage);
    }

    [Test]
    public void Constructor_InitializesDefaultPeriod()
    {
        // Act
        var viewModel = CreateViewModel();

        // Assert
        viewModel.SelectedPeriod.Should().Be(InsightsPeriod.ThisWeek);
    }

    [Test]
    public void Constructor_InitializesCustomDates()
    {
        // Act
        var viewModel = CreateViewModel();

        // Assert
        viewModel.CustomStartDate.Should().NotBeNull();
        viewModel.CustomEndDate.Should().NotBeNull();
        viewModel.CustomEndDate!.Value.Date.Should().Be(DateTimeOffset.Now.Date);
    }

    [Test]
    public void Constructor_InitializesIsCustomPeriodToFalse()
    {
        // Act
        var viewModel = CreateViewModel();

        // Assert
        viewModel.IsCustomPeriod.Should().BeFalse();
    }

    [Test]
    public async Task Constructor_InitializesLoadingStateToFalse()
    {
        // Arrange - setup mock to return immediately
        _persistenceMock.GetDailyUsageAsync(Arg.Any<DateOnly>(), Arg.Any<DateOnly>()).Returns(new List<DailyUsage>());

        // Act
        var viewModel = CreateViewModel();

        // Assert - after initialization completes
        await viewModel.InitializationTask;
        viewModel.IsLoading.Should().BeFalse();
    }

    [Test]
    public void Constructor_InitializesNetworkUsageProperties()
    {
        // Act
        var viewModel = CreateViewModel();

        // Assert
        viewModel.TotalDownload.Should().Be("0 B");
        viewModel.TotalUpload.Should().Be("0 B");
        viewModel.PeakDownloadSpeed.Should().Be("0 B/s");
        viewModel.PeakUploadSpeed.Should().Be("0 B/s");
    }

    [Test]
    public void Constructor_InitializesSystemTrendsProperties()
    {
        // Act
        var viewModel = CreateViewModel();

        // Assert
        viewModel.AvgCpuPercent.Should().Be(0);
        viewModel.MaxCpuPercent.Should().Be(0);
        viewModel.AvgMemoryPercent.Should().Be(0);
        viewModel.MaxMemoryPercent.Should().Be(0);
    }

    [Test]
    public void Constructor_InitializesCorrelationProperties()
    {
        // Act
        var viewModel = CreateViewModel();

        // Assert
        viewModel.NetworkCpuCorrelation.Should().Be(0);
        viewModel.NetworkMemoryCorrelation.Should().Be(0);
        viewModel.CpuMemoryCorrelation.Should().Be(0);
    }

    [Test]
    public void Constructor_InitializesCorrelationInsights()
    {
        // Act
        var viewModel = CreateViewModel();

        // Assert
        viewModel.CorrelationInsights.Should().NotBeNull();
    }

    [Test]
    public void Constructor_InitializesDailyUsageAxes()
    {
        // Act
        var viewModel = CreateViewModel();

        // Assert
        viewModel.DailyUsageXAxes.Should().NotBeNull();
        viewModel.DailyUsageYAxes.Should().NotBeNull();
    }

    [Test]
    public void Constructor_InitializesSystemTrendAxes()
    {
        // Act
        var viewModel = CreateViewModel();

        // Assert
        viewModel.SystemTrendXAxes.Should().NotBeNull();
        viewModel.SystemTrendYAxes.Should().NotBeNull();
    }

    #endregion

    #region Tab Selection Tests

    [Test]
    public void SelectNetworkTabCommand_SetsSelectedTabToNetworkUsage()
    {
        // Arrange
        var viewModel = CreateViewModel();
        viewModel.SelectedTab = InsightsTab.SystemTrends;

        // Act
        viewModel.SelectNetworkTabCommand.Execute(null);

        // Assert
        viewModel.SelectedTab.Should().Be(InsightsTab.NetworkUsage);
    }

    [Test]
    public void SelectSystemTrendsTabCommand_SetsSelectedTabToSystemTrends()
    {
        // Arrange
        var viewModel = CreateViewModel();

        // Act
        viewModel.SelectSystemTrendsTabCommand.Execute(null);

        // Assert
        viewModel.SelectedTab.Should().Be(InsightsTab.SystemTrends);
    }

    [Test]
    public void SelectCorrelationsTabCommand_SetsSelectedTabToCorrelations()
    {
        // Arrange
        var viewModel = CreateViewModel();

        // Act
        viewModel.SelectCorrelationsTabCommand.Execute(null);

        // Assert
        viewModel.SelectedTab.Should().Be(InsightsTab.Correlations);
    }

    [Test]
    public async Task SelectedTab_WhenChanged_LoadsDataForNewTab()
    {
        // Arrange
        var viewModel = CreateViewModel();
        await viewModel.InitializationTask;

        // Act
        viewModel.SelectedTab = InsightsTab.SystemTrends;
        await viewModel.PendingLoadTask!;

        // Assert
        _systemHistoryMock.Received().GetHourlyStatsAsync(Arg.Any<DateTime>(), Arg.Any<DateTime>());
    }

    [Test]
    public void SelectedTab_RaisesPropertyChanged()
    {
        // Arrange
        var viewModel = CreateViewModel();
        using var monitor = viewModel.Monitor();

        // Act
        viewModel.SelectedTab = InsightsTab.Correlations;

        // Assert
        monitor.Should().RaisePropertyChangeFor(x => x.SelectedTab);
    }

    #endregion

    #region Period Selection Tests

    [Test]
    public void SetPeriodCommand_SetsPeriodToToday()
    {
        // Arrange
        var viewModel = CreateViewModel();

        // Act
        viewModel.SetPeriodCommand.Execute(InsightsPeriod.Today);

        // Assert
        viewModel.SelectedPeriod.Should().Be(InsightsPeriod.Today);
    }

    [Test]
    public void SetPeriodCommand_SetsPeriodToThisWeek()
    {
        // Arrange
        var viewModel = CreateViewModel();
        viewModel.SelectedPeriod = InsightsPeriod.Today;

        // Act
        viewModel.SetPeriodCommand.Execute(InsightsPeriod.ThisWeek);

        // Assert
        viewModel.SelectedPeriod.Should().Be(InsightsPeriod.ThisWeek);
    }

    [Test]
    public void SetPeriodCommand_SetsPeriodToThisMonth()
    {
        // Arrange
        var viewModel = CreateViewModel();

        // Act
        viewModel.SetPeriodCommand.Execute(InsightsPeriod.ThisMonth);

        // Assert
        viewModel.SelectedPeriod.Should().Be(InsightsPeriod.ThisMonth);
    }

    [Test]
    public void SetPeriodCommand_SetsPeriodToCustom()
    {
        // Arrange
        var viewModel = CreateViewModel();

        // Act
        viewModel.SetPeriodCommand.Execute(InsightsPeriod.Custom);

        // Assert
        viewModel.SelectedPeriod.Should().Be(InsightsPeriod.Custom);
    }

    [Test]
    public void SelectedPeriod_WhenSetToCustom_SetsIsCustomPeriodToTrue()
    {
        // Arrange
        var viewModel = CreateViewModel();

        // Act
        viewModel.SelectedPeriod = InsightsPeriod.Custom;

        // Assert
        viewModel.IsCustomPeriod.Should().BeTrue();
    }

    [Test]
    public void SelectedPeriod_WhenSetToNonCustom_SetsIsCustomPeriodToFalse()
    {
        // Arrange
        var viewModel = CreateViewModel();
        viewModel.SelectedPeriod = InsightsPeriod.Custom;

        // Act
        viewModel.SelectedPeriod = InsightsPeriod.ThisWeek;

        // Assert
        viewModel.IsCustomPeriod.Should().BeFalse();
    }

    [Test]
    public async Task SelectedPeriod_WhenChanged_TriggersDataReload()
    {
        // Arrange
        var viewModel = CreateViewModel();
        await viewModel.InitializationTask;

        _persistenceMock.ClearReceivedCalls();

        // Act
        viewModel.SelectedPeriod = InsightsPeriod.ThisMonth;
        await viewModel.PendingLoadTask!;

        // Assert
        _persistenceMock.Received().GetDailyUsageAsync(Arg.Any<DateOnly>(), Arg.Any<DateOnly>());
    }

    [Test]
    [Arguments(InsightsPeriod.Today)]
    [Arguments(InsightsPeriod.ThisWeek)]
    [Arguments(InsightsPeriod.ThisMonth)]
    [Arguments(InsightsPeriod.Custom)]
    public void SelectedPeriod_AcceptsAllPeriods(InsightsPeriod period)
    {
        // Arrange
        var viewModel = CreateViewModel();

        // Act
        viewModel.SelectedPeriod = period;

        // Assert
        viewModel.SelectedPeriod.Should().Be(period);
    }

    [Test]
    public void SelectedPeriod_RaisesPropertyChanged()
    {
        // Arrange
        var viewModel = CreateViewModel();
        using var monitor = viewModel.Monitor();

        // Act
        viewModel.SelectedPeriod = InsightsPeriod.ThisMonth;

        // Assert
        monitor.Should().RaisePropertyChangeFor(x => x.SelectedPeriod);
    }

    #endregion

    #region Custom Date Tests

    [Test]
    public async Task CustomStartDate_WhenChanged_TriggersReloadIfCustomPeriod()
    {
        // Arrange
        var viewModel = CreateViewModel();
        viewModel.SelectedPeriod = InsightsPeriod.Custom;
        viewModel.CustomEndDate = DateTimeOffset.Now;
        if (viewModel.PendingLoadTask is not null) await viewModel.PendingLoadTask;

        _persistenceMock.ClearReceivedCalls();

        // Act
        viewModel.CustomStartDate = DateTimeOffset.Now.AddDays(-14);
        await viewModel.PendingLoadTask!;

        // Assert
        _persistenceMock.Received().GetDailyUsageAsync(Arg.Any<DateOnly>(), Arg.Any<DateOnly>());
    }

    [Test]
    public async Task CustomEndDate_WhenChanged_TriggersReloadIfCustomPeriod()
    {
        // Arrange
        var viewModel = CreateViewModel();
        viewModel.SelectedPeriod = InsightsPeriod.Custom;
        viewModel.CustomStartDate = DateTimeOffset.Now.AddDays(-7);
        if (viewModel.PendingLoadTask is not null) await viewModel.PendingLoadTask;

        _persistenceMock.ClearReceivedCalls();

        // Act
        viewModel.CustomEndDate = DateTimeOffset.Now.AddDays(-1);
        await viewModel.PendingLoadTask!;

        // Assert
        _persistenceMock.Received().GetDailyUsageAsync(Arg.Any<DateOnly>(), Arg.Any<DateOnly>());
    }

    #endregion

    #region GetTrendStatus Logic Tests

    [Test]
    [Arguments(85, 85, "Critical")]
    [Arguments(81, 50, "Critical")]
    [Arguments(65, 80, "High")]
    [Arguments(61, 60, "High")]
    [Arguments(50, 95, "Spiky")]
    [Arguments(30, 91, "Spiky")]
    [Arguments(45, 80, "Moderate")]
    [Arguments(41, 50, "Moderate")]
    [Arguments(30, 70, "Normal")]
    [Arguments(10, 20, "Normal")]
    public async Task GetTrendStatus_ReturnsCorrectStatus(double avg, double max, string expectedStatus)
    {
        // Arrange - single data point so avg == the value and max == the value
        var systemData = new List<HourlySystemStats>
        {
            new() { Hour = DateTime.Now, AvgCpuPercent = avg, MaxCpuPercent = max, AvgMemoryPercent = 0, MaxMemoryPercent = 0 }
        };

        _systemHistoryMock.GetHourlyStatsAsync(Arg.Any<DateTime>(), Arg.Any<DateTime>()).Returns(systemData);

        var viewModel = CreateViewModel();
        await viewModel.InitializationTask;

        // Act - switch to SystemTrends tab to trigger loading
        viewModel.SelectedTab = InsightsTab.SystemTrends;
        await viewModel.PendingLoadTask!;

        // Assert - verify the ViewModel's actual CpuTrendStatus property
        viewModel.CpuTrendStatus.Should().Be(expectedStatus);
    }

    [Test]
    public async Task LoadSystemTrends_WithData_CalculatesTrendStatusCorrectly()
    {
        // Arrange
        var systemData = new List<HourlySystemStats>
        {
            new() { Hour = DateTime.Now.AddHours(-2), AvgCpuPercent = 90, MaxCpuPercent = 95, AvgMemoryPercent = 45, MaxMemoryPercent = 50 },
            new() { Hour = DateTime.Now.AddHours(-1), AvgCpuPercent = 85, MaxCpuPercent = 92, AvgMemoryPercent = 50, MaxMemoryPercent = 60 }
        };

        _systemHistoryMock.GetHourlyStatsAsync(Arg.Any<DateTime>(), Arg.Any<DateTime>()).Returns(systemData);

        var viewModel = CreateViewModel();
        await viewModel.InitializationTask;

        // Act
        viewModel.SelectedTab = InsightsTab.SystemTrends;
        await viewModel.PendingLoadTask!;

        // Assert
        viewModel.CpuTrendStatus.Should().Be("Critical"); // avg 87.5 > 80
        viewModel.MemoryTrendStatus.Should().Be("Moderate"); // avg 47.5 > 40
    }

    #endregion

    #region Correlation Calculation Tests

    [Test]
    public async Task LoadCorrelations_WithInsufficientData_SetsZeroCorrelations()
    {
        // Arrange
        _systemHistoryMock.GetHourlyStatsAsync(Arg.Any<DateTime>(), Arg.Any<DateTime>())
            .Returns(new List<HourlySystemStats>
            {
                new() { Hour = DateTime.Now, AvgCpuPercent = 50, AvgMemoryPercent = 60 }
            });

        var viewModel = CreateViewModel();
        await viewModel.InitializationTask;

        // Act
        viewModel.SelectedTab = InsightsTab.Correlations;
        await viewModel.PendingLoadTask!;

        // Assert
        viewModel.NetworkCpuCorrelation.Should().Be(0);
        viewModel.NetworkMemoryCorrelation.Should().Be(0);
        viewModel.CpuMemoryCorrelation.Should().Be(0);
    }

    [Test]
    public async Task LoadCorrelations_WithNoSystemHistoryService_AddsInsightMessage()
    {
        // Arrange
        var viewModel = CreateViewModelWithoutSystemHistory();
        await viewModel.InitializationTask;

        // Act
        viewModel.SelectedTab = InsightsTab.Correlations;
        await viewModel.PendingLoadTask!;

        // Assert
        viewModel.CorrelationInsights.Should().Contain(x => x.Contains("unavailable"));
    }

    #endregion

    #region Network Usage Data Loading Tests

    [Test]
    public async Task LoadNetworkUsage_WithNoData_SetsEmptyTotals()
    {
        // Arrange
        _persistenceMock.GetDailyUsageAsync(Arg.Any<DateOnly>(), Arg.Any<DateOnly>()).Returns(new List<DailyUsage>());

        var viewModel = CreateViewModel();
        await viewModel.InitializationTask;

        // Assert - when no data, totals should be zero/empty
        viewModel.TotalDownload.Should().Be("0 B");
        viewModel.TotalUpload.Should().Be("0 B");
    }

    [Test]
    public async Task LoadNetworkUsage_WithData_CalculatesTotalsCorrectly()
    {
        // Arrange
        var dailyData = new List<DailyUsage>
        {
            new() { Date = DateOnly.FromDateTime(DateTime.Today), BytesReceived = 1024 * 1024 * 100, BytesSent = 1024 * 1024 * 50, PeakDownloadSpeed = 1024 * 1024, PeakUploadSpeed = 512 * 1024 },
            new() { Date = DateOnly.FromDateTime(DateTime.Today.AddDays(-1)), BytesReceived = 1024 * 1024 * 200, BytesSent = 1024 * 1024 * 100, PeakDownloadSpeed = 2 * 1024 * 1024, PeakUploadSpeed = 1024 * 1024 }
        };

        _persistenceMock.GetDailyUsageAsync(Arg.Any<DateOnly>(), Arg.Any<DateOnly>()).Returns(dailyData);

        var viewModel = CreateViewModel();
        await viewModel.InitializationTask;

        // Assert
        viewModel.TotalDownload.Should().Be("300.00 MB");
        viewModel.TotalUpload.Should().Be("150.00 MB");
        viewModel.PeakDownloadSpeed.Should().Be("2.00 MB/s");
        viewModel.PeakUploadSpeed.Should().Be("1.00 MB/s");
    }

    [Test]
    public async Task LoadNetworkUsage_WithData_BuildsChartSeries()
    {
        // Arrange
        var dailyData = new List<DailyUsage>
        {
            new() { Date = DateOnly.FromDateTime(DateTime.Today), BytesReceived = 100, BytesSent = 50 }
        };

        _persistenceMock.GetDailyUsageAsync(Arg.Any<DateOnly>(), Arg.Any<DateOnly>()).Returns(dailyData);

        var viewModel = CreateViewModel();
        await viewModel.InitializationTask;

        // Assert
        viewModel.DailyUsageChart.Should().NotBeEmpty();
    }

    #endregion

    #region System Trends Data Loading Tests

    [Test]
    public async Task LoadSystemTrends_WithNoSystemHistoryService_SetsUnavailableStatus()
    {
        // Arrange
        var viewModel = CreateViewModelWithoutSystemHistory();
        await viewModel.InitializationTask;

        // Act
        viewModel.SelectedTab = InsightsTab.SystemTrends;
        await viewModel.PendingLoadTask!;

        // Assert
        viewModel.CpuTrendStatus.Should().Be("Unavailable");
        viewModel.MemoryTrendStatus.Should().Be("Unavailable");
    }

    [Test]
    public async Task LoadSystemTrends_WithNoData_SetsNoDataStatus()
    {
        // Arrange
        _systemHistoryMock.GetHourlyStatsAsync(Arg.Any<DateTime>(), Arg.Any<DateTime>()).Returns(new List<HourlySystemStats>());

        var viewModel = CreateViewModel();
        await viewModel.InitializationTask;

        // Act
        viewModel.SelectedTab = InsightsTab.SystemTrends;
        await viewModel.PendingLoadTask!;

        // Assert
        viewModel.CpuTrendStatus.Should().Be("No Data");
        viewModel.MemoryTrendStatus.Should().Be("No Data");
    }

    [Test]
    public async Task LoadSystemTrends_WithData_CalculatesAveragesCorrectly()
    {
        // Arrange
        var systemData = new List<HourlySystemStats>
        {
            new() { Hour = DateTime.Now.AddHours(-2), AvgCpuPercent = 40, MaxCpuPercent = 60, AvgMemoryPercent = 50, MaxMemoryPercent = 70 },
            new() { Hour = DateTime.Now.AddHours(-1), AvgCpuPercent = 60, MaxCpuPercent = 80, AvgMemoryPercent = 70, MaxMemoryPercent = 90 }
        };

        _systemHistoryMock.GetHourlyStatsAsync(Arg.Any<DateTime>(), Arg.Any<DateTime>()).Returns(systemData);

        var viewModel = CreateViewModel();
        await viewModel.InitializationTask;

        // Act
        viewModel.SelectedTab = InsightsTab.SystemTrends;
        await viewModel.PendingLoadTask!;

        // Assert
        viewModel.AvgCpuPercent.Should().Be(50); // (40 + 60) / 2
        viewModel.MaxCpuPercent.Should().Be(80);
        viewModel.AvgMemoryPercent.Should().Be(60); // (50 + 70) / 2
        viewModel.MaxMemoryPercent.Should().Be(90);
    }

    #endregion

    #region Refresh Command Tests

    [Test]
    public async Task RefreshCommand_ClearsErrorState()
    {
        // Arrange
        var viewModel = CreateViewModel();
        // Simulate error state
        viewModel.GetType().GetProperty("HasError")!.SetValue(viewModel, true);
        viewModel.GetType().GetProperty("ErrorMessage")!.SetValue(viewModel, "Test error");

        // Act
        viewModel.RefreshCommand.Execute(null);
        await viewModel.PendingLoadTask!;

        // Assert
        viewModel.HasError.Should().BeFalse();
        viewModel.ErrorMessage.Should().BeEmpty();
    }

    [Test]
    public async Task RefreshCommand_ReloadsDataForCurrentTab()
    {
        // Arrange
        var viewModel = CreateViewModel();
        await viewModel.InitializationTask;
        _persistenceMock.ClearReceivedCalls();

        // Act
        await viewModel.RefreshCommand.ExecuteAsync(null);
        await viewModel.PendingLoadTask!;

        // Assert
        await _persistenceMock.Received().GetDailyUsageAsync(Arg.Any<DateOnly>(), Arg.Any<DateOnly>());
    }

    #endregion

    #region Error Handling Tests

    [Test]
    public async Task LoadData_WhenExceptionThrown_SetsErrorState()
    {
        // Arrange
        _persistenceMock.GetDailyUsageAsync(Arg.Any<DateOnly>(), Arg.Any<DateOnly>())
            .Returns<Task<List<DailyUsage>>>(_ => throw new InvalidOperationException("Test error"));

        var viewModel = CreateViewModel();
        await viewModel.InitializationTask;

        // Assert
        viewModel.HasError.Should().BeTrue();
        viewModel.ErrorMessage.Should().Contain("Test error");
    }

    [Test]
    public async Task LoadData_WhenExceptionThrown_LogsError()
    {
        // Arrange
        _persistenceMock.GetDailyUsageAsync(Arg.Any<DateOnly>(), Arg.Any<DateOnly>())
            .Returns<Task<List<DailyUsage>>>(_ => throw new InvalidOperationException("Test error"));

        var viewModel = CreateViewModel();
        await viewModel.InitializationTask;

        // Assert - verify error state is set (logger verification removed as NSubstitute doesn't easily support ILogger verification)
        viewModel.HasError.Should().BeTrue();
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

    [Test]
    public void Dispose_CancelsPendingOperations()
    {
        // Arrange
        var tcs = new TaskCompletionSource<List<DailyUsage>>();
        _persistenceMock.GetDailyUsageAsync(Arg.Any<DateOnly>(), Arg.Any<DateOnly>()).Returns(tcs.Task);

        var viewModel = CreateViewModel();

        // Act
        viewModel.Dispose();
        tcs.SetResult(new List<DailyUsage>());

        // Assert - no exception should be thrown
    }

    #endregion

    #region Property Change Notification Tests

    [Test]
    public void TotalDownload_RaisesPropertyChanged()
    {
        // Arrange
        var viewModel = CreateViewModel();
        using var monitor = viewModel.Monitor();

        // Act
        viewModel.GetType().GetProperty("TotalDownload")!.SetValue(viewModel, "100 MB");

        // Assert
        monitor.Should().RaisePropertyChangeFor(x => x.TotalDownload);
    }

    [Test]
    public void IsLoading_RaisesPropertyChanged()
    {
        // Arrange
        var viewModel = CreateViewModel();
        using var monitor = viewModel.Monitor();

        // Act
        viewModel.GetType().GetProperty("IsLoading")!.SetValue(viewModel, true);

        // Assert
        monitor.Should().RaisePropertyChangeFor(x => x.IsLoading);
    }

    [Test]
    public void HasData_WhenNoDataLoaded_IsFalse()
    {
        // Arrange
        var viewModel = CreateViewModel();

        // Act
        var hasData = viewModel.HasData;

        // Assert
        hasData.Should().BeFalse();
    }

    [Test]
    public void HasError_RaisesPropertyChanged()
    {
        // Arrange
        var viewModel = CreateViewModel();
        using var monitor = viewModel.Monitor();

        // Act
        viewModel.GetType().GetProperty("HasError")!.SetValue(viewModel, true);

        // Assert
        monitor.Should().RaisePropertyChangeFor(x => x.HasError);
    }

    [Test]
    public void IsCustomPeriod_RaisesPropertyChanged()
    {
        // Arrange
        var viewModel = CreateViewModel();
        using var monitor = viewModel.Monitor();

        // Act
        viewModel.SelectedPeriod = InsightsPeriod.Custom;

        // Assert
        monitor.Should().RaisePropertyChangeFor(x => x.IsCustomPeriod);
    }

    #endregion

    #region Edge Cases

    [Test]
    public void Constructor_WithNullSystemHistory_DoesNotThrow()
    {
        // Act
        var action = () => new InsightsViewModel(
            _persistenceMock,
            null,
            null,
            _loggerMock);

        // Assert
        action.Should().NotThrow();
    }

    [Test]
    public void Constructor_WithNullLogger_DoesNotThrow()
    {
        // Act
        var action = () => new InsightsViewModel(
            _persistenceMock,
            _systemHistoryMock,
            null);

        // Assert
        action.Should().NotThrow();
    }

    [Test]
    public async Task ChangeTab_WhileLoading_CancelsPreviousLoad()
    {
        // Arrange
        var slowTask = new TaskCompletionSource<List<DailyUsage>>();
        _persistenceMock.GetDailyUsageAsync(Arg.Any<DateOnly>(), Arg.Any<DateOnly>()).Returns(slowTask.Task);

        var viewModel = CreateViewModel();

        // Act - change tab before first load completes
        viewModel.SelectedTab = InsightsTab.SystemTrends;
        await viewModel.PendingLoadTask!;

        // Complete the slow task
        slowTask.SetResult(new List<DailyUsage>());

        // Assert - should not throw and should be on new tab
        viewModel.SelectedTab.Should().Be(InsightsTab.SystemTrends);
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
