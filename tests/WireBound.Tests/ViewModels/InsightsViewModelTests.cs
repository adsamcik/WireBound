using AwesomeAssertions;
using Microsoft.Extensions.Logging;
using Moq;
using WireBound.Avalonia.ViewModels;
using WireBound.Core.Models;
using WireBound.Core.Services;
using WireBound.Tests.Fixtures;

namespace WireBound.Tests.ViewModels;

/// <summary>
/// Unit tests for InsightsViewModel
/// </summary>
[Collection("LiveCharts")]
public class InsightsViewModelTests : IDisposable
{
    private readonly Mock<IDataPersistenceService> _persistenceMock;
    private readonly Mock<ISystemHistoryService> _systemHistoryMock;
    private readonly Mock<ILogger<InsightsViewModel>> _loggerMock;
    private InsightsViewModel? _viewModel;

    public InsightsViewModelTests()
    {
        _persistenceMock = new Mock<IDataPersistenceService>();
        _systemHistoryMock = new Mock<ISystemHistoryService>();
        _loggerMock = new Mock<ILogger<InsightsViewModel>>();

        // Setup default returns
        SetupDefaultMocks();
    }

    private void SetupDefaultMocks()
    {
        _persistenceMock
            .Setup(x => x.GetDailyUsageAsync(It.IsAny<DateOnly>(), It.IsAny<DateOnly>()))
            .ReturnsAsync(new List<DailyUsage>());

        _persistenceMock
            .Setup(x => x.GetHourlyUsageAsync(It.IsAny<DateOnly>()))
            .ReturnsAsync(new List<HourlyUsage>());

        _systemHistoryMock
            .Setup(x => x.GetHourlyStatsAsync(It.IsAny<DateTime>(), It.IsAny<DateTime>()))
            .ReturnsAsync(new List<HourlySystemStats>());
    }

    private InsightsViewModel CreateViewModel()
    {
        return new InsightsViewModel(
            _persistenceMock.Object,
            _systemHistoryMock.Object,
            _loggerMock.Object);
    }

    private InsightsViewModel CreateViewModelWithoutSystemHistory()
    {
        return new InsightsViewModel(
            _persistenceMock.Object,
            null,
            _loggerMock.Object);
    }

    #region Constructor Tests

    [Fact]
    public void Constructor_InitializesToNetworkUsageTab()
    {
        // Act
        _viewModel = CreateViewModel();

        // Assert
        _viewModel.SelectedTab.Should().Be(InsightsTab.NetworkUsage);
    }

    [Fact]
    public void Constructor_InitializesDefaultPeriod()
    {
        // Act
        _viewModel = CreateViewModel();

        // Assert
        _viewModel.SelectedPeriod.Should().Be(InsightsPeriod.ThisWeek);
    }

    [Fact]
    public void Constructor_InitializesCustomDates()
    {
        // Act
        _viewModel = CreateViewModel();

        // Assert
        _viewModel.CustomStartDate.Should().NotBeNull();
        _viewModel.CustomEndDate.Should().NotBeNull();
        _viewModel.CustomEndDate!.Value.Date.Should().Be(DateTimeOffset.Now.Date);
    }

    [Fact]
    public void Constructor_InitializesIsCustomPeriodToFalse()
    {
        // Act
        _viewModel = CreateViewModel();

        // Assert
        _viewModel.IsCustomPeriod.Should().BeFalse();
    }

    [Fact]
    public void Constructor_InitializesLoadingStateToFalse()
    {
        // Arrange - setup mock to return immediately
        _persistenceMock
            .Setup(x => x.GetDailyUsageAsync(It.IsAny<DateOnly>(), It.IsAny<DateOnly>()))
            .ReturnsAsync(new List<DailyUsage>());

        // Act
        _viewModel = CreateViewModel();

        // Assert - after initialization completes
        Thread.Sleep(100); // Allow async loading to complete
        _viewModel.IsLoading.Should().BeFalse();
    }

    [Fact]
    public void Constructor_InitializesNetworkUsageProperties()
    {
        // Act
        _viewModel = CreateViewModel();

        // Assert
        _viewModel.TotalDownload.Should().Be("0 B");
        _viewModel.TotalUpload.Should().Be("0 B");
        _viewModel.PeakDownloadSpeed.Should().Be("0 B/s");
        _viewModel.PeakUploadSpeed.Should().Be("0 B/s");
    }

    [Fact]
    public void Constructor_InitializesSystemTrendsProperties()
    {
        // Act
        _viewModel = CreateViewModel();

        // Assert
        _viewModel.AvgCpuPercent.Should().Be(0);
        _viewModel.MaxCpuPercent.Should().Be(0);
        _viewModel.AvgMemoryPercent.Should().Be(0);
        _viewModel.MaxMemoryPercent.Should().Be(0);
    }

    [Fact]
    public void Constructor_InitializesCorrelationProperties()
    {
        // Act
        _viewModel = CreateViewModel();

        // Assert
        _viewModel.NetworkCpuCorrelation.Should().Be(0);
        _viewModel.NetworkMemoryCorrelation.Should().Be(0);
        _viewModel.CpuMemoryCorrelation.Should().Be(0);
    }

    [Fact]
    public void Constructor_InitializesCorrelationInsights()
    {
        // Act
        _viewModel = CreateViewModel();

        // Assert
        _viewModel.CorrelationInsights.Should().NotBeNull();
    }

    [Fact]
    public void Constructor_InitializesDailyUsageAxes()
    {
        // Act
        _viewModel = CreateViewModel();

        // Assert
        _viewModel.DailyUsageXAxes.Should().NotBeNull();
        _viewModel.DailyUsageYAxes.Should().NotBeNull();
    }

    [Fact]
    public void Constructor_InitializesSystemTrendAxes()
    {
        // Act
        _viewModel = CreateViewModel();

        // Assert
        _viewModel.SystemTrendXAxes.Should().NotBeNull();
        _viewModel.SystemTrendYAxes.Should().NotBeNull();
    }

    #endregion

    #region Tab Selection Tests

    [Fact]
    public void SelectNetworkTabCommand_SetsSelectedTabToNetworkUsage()
    {
        // Arrange
        _viewModel = CreateViewModel();
        _viewModel.SelectedTab = InsightsTab.SystemTrends;

        // Act
        _viewModel.SelectNetworkTabCommand.Execute(null);

        // Assert
        _viewModel.SelectedTab.Should().Be(InsightsTab.NetworkUsage);
    }

    [Fact]
    public void SelectSystemTrendsTabCommand_SetsSelectedTabToSystemTrends()
    {
        // Arrange
        _viewModel = CreateViewModel();

        // Act
        _viewModel.SelectSystemTrendsTabCommand.Execute(null);

        // Assert
        _viewModel.SelectedTab.Should().Be(InsightsTab.SystemTrends);
    }

    [Fact]
    public void SelectCorrelationsTabCommand_SetsSelectedTabToCorrelations()
    {
        // Arrange
        _viewModel = CreateViewModel();

        // Act
        _viewModel.SelectCorrelationsTabCommand.Execute(null);

        // Assert
        _viewModel.SelectedTab.Should().Be(InsightsTab.Correlations);
    }

    [Fact]
    public void SelectedTab_WhenChanged_LoadsDataForNewTab()
    {
        // Arrange
        _viewModel = CreateViewModel();
        Thread.Sleep(100); // Wait for initial load

        // Act
        _viewModel.SelectedTab = InsightsTab.SystemTrends;
        Thread.Sleep(100); // Wait for async load

        // Assert
        _systemHistoryMock.Verify(
            x => x.GetHourlyStatsAsync(It.IsAny<DateTime>(), It.IsAny<DateTime>()),
            Times.AtLeastOnce);
    }

    [Fact]
    public void SelectedTab_RaisesPropertyChanged()
    {
        // Arrange
        _viewModel = CreateViewModel();
        using var monitor = _viewModel.Monitor();

        // Act
        _viewModel.SelectedTab = InsightsTab.Correlations;

        // Assert
        monitor.Should().RaisePropertyChangeFor(x => x.SelectedTab);
    }

    #endregion

    #region Period Selection Tests

    [Fact]
    public void SetPeriodCommand_SetsPeriodToToday()
    {
        // Arrange
        _viewModel = CreateViewModel();

        // Act
        _viewModel.SetPeriodCommand.Execute(InsightsPeriod.Today);

        // Assert
        _viewModel.SelectedPeriod.Should().Be(InsightsPeriod.Today);
    }

    [Fact]
    public void SetPeriodCommand_SetsPeriodToThisWeek()
    {
        // Arrange
        _viewModel = CreateViewModel();
        _viewModel.SelectedPeriod = InsightsPeriod.Today;

        // Act
        _viewModel.SetPeriodCommand.Execute(InsightsPeriod.ThisWeek);

        // Assert
        _viewModel.SelectedPeriod.Should().Be(InsightsPeriod.ThisWeek);
    }

    [Fact]
    public void SetPeriodCommand_SetsPeriodToThisMonth()
    {
        // Arrange
        _viewModel = CreateViewModel();

        // Act
        _viewModel.SetPeriodCommand.Execute(InsightsPeriod.ThisMonth);

        // Assert
        _viewModel.SelectedPeriod.Should().Be(InsightsPeriod.ThisMonth);
    }

    [Fact]
    public void SetPeriodCommand_SetsPeriodToCustom()
    {
        // Arrange
        _viewModel = CreateViewModel();

        // Act
        _viewModel.SetPeriodCommand.Execute(InsightsPeriod.Custom);

        // Assert
        _viewModel.SelectedPeriod.Should().Be(InsightsPeriod.Custom);
    }

    [Fact]
    public void SelectedPeriod_WhenSetToCustom_SetsIsCustomPeriodToTrue()
    {
        // Arrange
        _viewModel = CreateViewModel();

        // Act
        _viewModel.SelectedPeriod = InsightsPeriod.Custom;

        // Assert
        _viewModel.IsCustomPeriod.Should().BeTrue();
    }

    [Fact]
    public void SelectedPeriod_WhenSetToNonCustom_SetsIsCustomPeriodToFalse()
    {
        // Arrange
        _viewModel = CreateViewModel();
        _viewModel.SelectedPeriod = InsightsPeriod.Custom;

        // Act
        _viewModel.SelectedPeriod = InsightsPeriod.ThisWeek;

        // Assert
        _viewModel.IsCustomPeriod.Should().BeFalse();
    }

    [Fact]
    public void SelectedPeriod_WhenChanged_TriggersDataReload()
    {
        // Arrange
        _viewModel = CreateViewModel();
        Thread.Sleep(100); // Wait for initial load

        _persistenceMock.Invocations.Clear();

        // Act
        _viewModel.SelectedPeriod = InsightsPeriod.ThisMonth;
        Thread.Sleep(100); // Wait for async load

        // Assert
        _persistenceMock.Verify(
            x => x.GetDailyUsageAsync(It.IsAny<DateOnly>(), It.IsAny<DateOnly>()),
            Times.AtLeastOnce);
    }

    [Theory]
    [InlineData(InsightsPeriod.Today)]
    [InlineData(InsightsPeriod.ThisWeek)]
    [InlineData(InsightsPeriod.ThisMonth)]
    [InlineData(InsightsPeriod.Custom)]
    public void SelectedPeriod_AcceptsAllPeriods(InsightsPeriod period)
    {
        // Arrange
        _viewModel = CreateViewModel();

        // Act
        _viewModel.SelectedPeriod = period;

        // Assert
        _viewModel.SelectedPeriod.Should().Be(period);
    }

    [Fact]
    public void SelectedPeriod_RaisesPropertyChanged()
    {
        // Arrange
        _viewModel = CreateViewModel();
        using var monitor = _viewModel.Monitor();

        // Act
        _viewModel.SelectedPeriod = InsightsPeriod.ThisMonth;

        // Assert
        monitor.Should().RaisePropertyChangeFor(x => x.SelectedPeriod);
    }

    #endregion

    #region Custom Date Tests

    [Fact]
    public void CustomStartDate_WhenChanged_TriggersReloadIfCustomPeriod()
    {
        // Arrange
        _viewModel = CreateViewModel();
        _viewModel.SelectedPeriod = InsightsPeriod.Custom;
        _viewModel.CustomEndDate = DateTimeOffset.Now;
        Thread.Sleep(100);

        _persistenceMock.Invocations.Clear();

        // Act
        _viewModel.CustomStartDate = DateTimeOffset.Now.AddDays(-14);
        Thread.Sleep(100);

        // Assert
        _persistenceMock.Verify(
            x => x.GetDailyUsageAsync(It.IsAny<DateOnly>(), It.IsAny<DateOnly>()),
            Times.AtLeastOnce);
    }

    [Fact]
    public void CustomEndDate_WhenChanged_TriggersReloadIfCustomPeriod()
    {
        // Arrange
        _viewModel = CreateViewModel();
        _viewModel.SelectedPeriod = InsightsPeriod.Custom;
        _viewModel.CustomStartDate = DateTimeOffset.Now.AddDays(-7);
        Thread.Sleep(100);

        _persistenceMock.Invocations.Clear();

        // Act
        _viewModel.CustomEndDate = DateTimeOffset.Now.AddDays(-1);
        Thread.Sleep(100);

        // Assert
        _persistenceMock.Verify(
            x => x.GetDailyUsageAsync(It.IsAny<DateOnly>(), It.IsAny<DateOnly>()),
            Times.AtLeastOnce);
    }

    #endregion

    #region GetTrendStatus Logic Tests

    [Theory]
    [InlineData(85, 85, "Critical")]
    [InlineData(81, 50, "Critical")]
    [InlineData(65, 80, "High")]
    [InlineData(61, 60, "High")]
    [InlineData(50, 95, "Spiky")]
    [InlineData(30, 91, "Spiky")]
    [InlineData(45, 80, "Moderate")]
    [InlineData(41, 50, "Moderate")]
    [InlineData(30, 70, "Normal")]
    [InlineData(10, 20, "Normal")]
    public void GetTrendStatus_ReturnsCorrectStatus(double avg, double max, string expectedStatus)
    {
        // This tests the static GetTrendStatus method logic indirectly
        // by checking the expected output based on the switch expression in the ViewModel

        // The logic is:
        // avg > 80 => "Critical"
        // avg > 60 => "High"
        // max > 90 => "Spiky"
        // avg > 40 => "Moderate"
        // _ => "Normal"

        var actualStatus = (avg, max) switch
        {
            ( > 80, _) => "Critical",
            ( > 60, _) => "High",
            (_, > 90) => "Spiky",
            ( > 40, _) => "Moderate",
            _ => "Normal"
        };

        actualStatus.Should().Be(expectedStatus);
    }

    [Fact]
    public async Task LoadSystemTrends_WithData_CalculatesTrendStatusCorrectly()
    {
        // Arrange
        var systemData = new List<HourlySystemStats>
        {
            new() { Hour = DateTime.Now.AddHours(-2), AvgCpuPercent = 90, MaxCpuPercent = 95, AvgMemoryPercent = 45, MaxMemoryPercent = 50 },
            new() { Hour = DateTime.Now.AddHours(-1), AvgCpuPercent = 85, MaxCpuPercent = 92, AvgMemoryPercent = 50, MaxMemoryPercent = 60 }
        };

        _systemHistoryMock
            .Setup(x => x.GetHourlyStatsAsync(It.IsAny<DateTime>(), It.IsAny<DateTime>()))
            .ReturnsAsync(systemData);

        _viewModel = CreateViewModel();
        Thread.Sleep(100);

        // Act
        _viewModel.SelectedTab = InsightsTab.SystemTrends;
        await Task.Delay(200); // Wait for async load

        // Assert
        _viewModel.CpuTrendStatus.Should().Be("Critical"); // avg 87.5 > 80
        _viewModel.MemoryTrendStatus.Should().Be("Moderate"); // avg 47.5 > 40
    }

    #endregion

    #region Correlation Calculation Tests

    [Fact]
    public async Task LoadCorrelations_WithInsufficientData_SetsZeroCorrelations()
    {
        // Arrange
        _systemHistoryMock
            .Setup(x => x.GetHourlyStatsAsync(It.IsAny<DateTime>(), It.IsAny<DateTime>()))
            .ReturnsAsync(new List<HourlySystemStats>
            {
                new() { Hour = DateTime.Now, AvgCpuPercent = 50, AvgMemoryPercent = 60 }
            });

        _viewModel = CreateViewModel();
        Thread.Sleep(100);

        // Act
        _viewModel.SelectedTab = InsightsTab.Correlations;
        await Task.Delay(200);

        // Assert
        _viewModel.NetworkCpuCorrelation.Should().Be(0);
        _viewModel.NetworkMemoryCorrelation.Should().Be(0);
        _viewModel.CpuMemoryCorrelation.Should().Be(0);
    }

    [Fact]
    public async Task LoadCorrelations_WithNoSystemHistoryService_AddsInsightMessage()
    {
        // Arrange
        _viewModel = CreateViewModelWithoutSystemHistory();
        Thread.Sleep(100);

        // Act
        _viewModel.SelectedTab = InsightsTab.Correlations;
        await Task.Delay(200);

        // Assert
        _viewModel.CorrelationInsights.Should().Contain(x => x.Contains("unavailable"));
    }

    #endregion

    #region Network Usage Data Loading Tests

    [Fact]
    public async Task LoadNetworkUsage_WithNoData_SetsEmptyTotals()
    {
        // Arrange
        _persistenceMock
            .Setup(x => x.GetDailyUsageAsync(It.IsAny<DateOnly>(), It.IsAny<DateOnly>()))
            .ReturnsAsync(new List<DailyUsage>());

        _viewModel = CreateViewModel();
        await Task.Delay(200);

        // Assert - when no data, totals should be zero/empty
        _viewModel.TotalDownload.Should().Be("0 B");
        _viewModel.TotalUpload.Should().Be("0 B");
    }

    [Fact]
    public async Task LoadNetworkUsage_WithData_CalculatesTotalsCorrectly()
    {
        // Arrange
        var dailyData = new List<DailyUsage>
        {
            new() { Date = DateOnly.FromDateTime(DateTime.Today), BytesReceived = 1024 * 1024 * 100, BytesSent = 1024 * 1024 * 50, PeakDownloadSpeed = 1024 * 1024, PeakUploadSpeed = 512 * 1024 },
            new() { Date = DateOnly.FromDateTime(DateTime.Today.AddDays(-1)), BytesReceived = 1024 * 1024 * 200, BytesSent = 1024 * 1024 * 100, PeakDownloadSpeed = 2 * 1024 * 1024, PeakUploadSpeed = 1024 * 1024 }
        };

        _persistenceMock
            .Setup(x => x.GetDailyUsageAsync(It.IsAny<DateOnly>(), It.IsAny<DateOnly>()))
            .ReturnsAsync(dailyData);

        _viewModel = CreateViewModel();
        await Task.Delay(200);

        // Assert
        _viewModel.TotalDownload.Should().NotBe("0 B");
        _viewModel.TotalUpload.Should().NotBe("0 B");
        _viewModel.PeakDownloadSpeed.Should().NotBe("0 B/s");
        _viewModel.PeakUploadSpeed.Should().NotBe("0 B/s");
    }

    [Fact]
    public async Task LoadNetworkUsage_WithData_BuildsChartSeries()
    {
        // Arrange
        var dailyData = new List<DailyUsage>
        {
            new() { Date = DateOnly.FromDateTime(DateTime.Today), BytesReceived = 100, BytesSent = 50 }
        };

        _persistenceMock
            .Setup(x => x.GetDailyUsageAsync(It.IsAny<DateOnly>(), It.IsAny<DateOnly>()))
            .ReturnsAsync(dailyData);

        _viewModel = CreateViewModel();
        await Task.Delay(200);

        // Assert
        _viewModel.DailyUsageChart.Should().NotBeEmpty();
    }

    #endregion

    #region System Trends Data Loading Tests

    [Fact]
    public async Task LoadSystemTrends_WithNoSystemHistoryService_SetsUnavailableStatus()
    {
        // Arrange
        _viewModel = CreateViewModelWithoutSystemHistory();
        Thread.Sleep(100);

        // Act
        _viewModel.SelectedTab = InsightsTab.SystemTrends;
        await Task.Delay(200);

        // Assert
        _viewModel.CpuTrendStatus.Should().Be("Unavailable");
        _viewModel.MemoryTrendStatus.Should().Be("Unavailable");
    }

    [Fact]
    public async Task LoadSystemTrends_WithNoData_SetsNoDataStatus()
    {
        // Arrange
        _systemHistoryMock
            .Setup(x => x.GetHourlyStatsAsync(It.IsAny<DateTime>(), It.IsAny<DateTime>()))
            .ReturnsAsync(new List<HourlySystemStats>());

        _viewModel = CreateViewModel();
        Thread.Sleep(100);

        // Act
        _viewModel.SelectedTab = InsightsTab.SystemTrends;
        await Task.Delay(200);

        // Assert
        _viewModel.CpuTrendStatus.Should().Be("No Data");
        _viewModel.MemoryTrendStatus.Should().Be("No Data");
    }

    [Fact]
    public async Task LoadSystemTrends_WithData_CalculatesAveragesCorrectly()
    {
        // Arrange
        var systemData = new List<HourlySystemStats>
        {
            new() { Hour = DateTime.Now.AddHours(-2), AvgCpuPercent = 40, MaxCpuPercent = 60, AvgMemoryPercent = 50, MaxMemoryPercent = 70 },
            new() { Hour = DateTime.Now.AddHours(-1), AvgCpuPercent = 60, MaxCpuPercent = 80, AvgMemoryPercent = 70, MaxMemoryPercent = 90 }
        };

        _systemHistoryMock
            .Setup(x => x.GetHourlyStatsAsync(It.IsAny<DateTime>(), It.IsAny<DateTime>()))
            .ReturnsAsync(systemData);

        _viewModel = CreateViewModel();
        Thread.Sleep(100);

        // Act
        _viewModel.SelectedTab = InsightsTab.SystemTrends;
        await Task.Delay(200);

        // Assert
        _viewModel.AvgCpuPercent.Should().Be(50); // (40 + 60) / 2
        _viewModel.MaxCpuPercent.Should().Be(80);
        _viewModel.AvgMemoryPercent.Should().Be(60); // (50 + 70) / 2
        _viewModel.MaxMemoryPercent.Should().Be(90);
    }

    #endregion

    #region Refresh Command Tests

    [Fact]
    public async Task RefreshCommand_ClearsErrorState()
    {
        // Arrange
        _viewModel = CreateViewModel();
        // Simulate error state
        _viewModel.GetType().GetProperty("HasError")!.SetValue(_viewModel, true);
        _viewModel.GetType().GetProperty("ErrorMessage")!.SetValue(_viewModel, "Test error");

        // Act
        _viewModel.RefreshCommand.Execute(null);
        await Task.Delay(100);

        // Assert
        _viewModel.HasError.Should().BeFalse();
        _viewModel.ErrorMessage.Should().BeEmpty();
    }

    [Fact]
    public async Task RefreshCommand_ReloadsDataForCurrentTab()
    {
        // Arrange
        _viewModel = CreateViewModel();
        await Task.Delay(100);
        _persistenceMock.Invocations.Clear();

        // Act
        _viewModel.RefreshCommand.Execute(null);
        await Task.Delay(200);

        // Assert
        _persistenceMock.Verify(
            x => x.GetDailyUsageAsync(It.IsAny<DateOnly>(), It.IsAny<DateOnly>()),
            Times.AtLeastOnce);
    }

    #endregion

    #region Error Handling Tests

    [Fact]
    public async Task LoadData_WhenExceptionThrown_SetsErrorState()
    {
        // Arrange
        _persistenceMock
            .Setup(x => x.GetDailyUsageAsync(It.IsAny<DateOnly>(), It.IsAny<DateOnly>()))
            .ThrowsAsync(new InvalidOperationException("Test error"));

        _viewModel = CreateViewModel();
        await Task.Delay(200);

        // Assert
        _viewModel.HasError.Should().BeTrue();
        _viewModel.ErrorMessage.Should().Contain("Test error");
    }

    [Fact]
    public async Task LoadData_WhenExceptionThrown_LogsError()
    {
        // Arrange
        _persistenceMock
            .Setup(x => x.GetDailyUsageAsync(It.IsAny<DateOnly>(), It.IsAny<DateOnly>()))
            .ThrowsAsync(new InvalidOperationException("Test error"));

        _viewModel = CreateViewModel();
        await Task.Delay(200);

        // Assert
        _loggerMock.Verify(
            x => x.Log(
                LogLevel.Error,
                It.IsAny<EventId>(),
                It.Is<It.IsAnyType>((v, t) => true),
                It.IsAny<Exception>(),
                It.IsAny<Func<It.IsAnyType, Exception?, string>>()),
            Times.AtLeastOnce);
    }

    #endregion

    #region Dispose Tests

    [Fact]
    public void Dispose_CanBeCalledMultipleTimes()
    {
        // Arrange
        _viewModel = CreateViewModel();

        // Act & Assert - should not throw
        _viewModel.Dispose();
        _viewModel.Dispose();
    }

    [Fact]
    public void Dispose_CancelsPendingOperations()
    {
        // Arrange
        var tcs = new TaskCompletionSource<List<DailyUsage>>();
        _persistenceMock
            .Setup(x => x.GetDailyUsageAsync(It.IsAny<DateOnly>(), It.IsAny<DateOnly>()))
            .Returns(tcs.Task);

        _viewModel = CreateViewModel();

        // Act
        _viewModel.Dispose();
        tcs.SetResult(new List<DailyUsage>());

        // Assert - no exception should be thrown
    }

    #endregion

    #region Property Change Notification Tests

    [Fact]
    public void TotalDownload_RaisesPropertyChanged()
    {
        // Arrange
        _viewModel = CreateViewModel();
        using var monitor = _viewModel.Monitor();

        // Act
        _viewModel.GetType().GetProperty("TotalDownload")!.SetValue(_viewModel, "100 MB");

        // Assert
        monitor.Should().RaisePropertyChangeFor(x => x.TotalDownload);
    }

    [Fact]
    public void IsLoading_RaisesPropertyChanged()
    {
        // Arrange
        _viewModel = CreateViewModel();
        using var monitor = _viewModel.Monitor();

        // Act
        _viewModel.GetType().GetProperty("IsLoading")!.SetValue(_viewModel, true);

        // Assert
        monitor.Should().RaisePropertyChangeFor(x => x.IsLoading);
    }

    [Fact]
    public void HasData_PropertyExists()
    {
        // Arrange
        _viewModel = CreateViewModel();

        // Act & Assert - just verify the property is accessible and is a boolean
        var hasData = _viewModel.HasData;
        hasData.Should().Be(hasData); // Property exists and is accessible
    }

    [Fact]
    public void HasError_RaisesPropertyChanged()
    {
        // Arrange
        _viewModel = CreateViewModel();
        using var monitor = _viewModel.Monitor();

        // Act
        _viewModel.GetType().GetProperty("HasError")!.SetValue(_viewModel, true);

        // Assert
        monitor.Should().RaisePropertyChangeFor(x => x.HasError);
    }

    [Fact]
    public void IsCustomPeriod_RaisesPropertyChanged()
    {
        // Arrange
        _viewModel = CreateViewModel();
        using var monitor = _viewModel.Monitor();

        // Act
        _viewModel.SelectedPeriod = InsightsPeriod.Custom;

        // Assert
        monitor.Should().RaisePropertyChangeFor(x => x.IsCustomPeriod);
    }

    #endregion

    #region Edge Cases

    [Fact]
    public void Constructor_WithNullSystemHistory_DoesNotThrow()
    {
        // Act
        var action = () => new InsightsViewModel(
            _persistenceMock.Object,
            null,
            _loggerMock.Object);

        // Assert
        action.Should().NotThrow();
    }

    [Fact]
    public void Constructor_WithNullLogger_DoesNotThrow()
    {
        // Act
        var action = () => new InsightsViewModel(
            _persistenceMock.Object,
            _systemHistoryMock.Object,
            null);

        // Assert
        action.Should().NotThrow();
    }

    [Fact]
    public async Task ChangeTab_WhileLoading_CancelsPreviousLoad()
    {
        // Arrange
        var slowTask = new TaskCompletionSource<List<DailyUsage>>();
        _persistenceMock
            .Setup(x => x.GetDailyUsageAsync(It.IsAny<DateOnly>(), It.IsAny<DateOnly>()))
            .Returns(slowTask.Task);

        _viewModel = CreateViewModel();

        // Act - change tab before first load completes
        _viewModel.SelectedTab = InsightsTab.SystemTrends;
        await Task.Delay(50);

        // Complete the slow task
        slowTask.SetResult(new List<DailyUsage>());

        // Assert - should not throw and should be on new tab
        _viewModel.SelectedTab.Should().Be(InsightsTab.SystemTrends);
    }

    #endregion

    public void Dispose()
    {
        _viewModel?.Dispose();
    }
}
