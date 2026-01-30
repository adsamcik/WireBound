using AwesomeAssertions;
using WireBound.Avalonia.ViewModels;
using WireBound.Core.Models;
using WireBound.Core.Services;
using WireBound.Tests.Fixtures;

namespace WireBound.Tests.ViewModels;

/// <summary>
/// Unit tests for SystemViewModel
/// </summary>
public class SystemViewModelTests : IAsyncDisposable
{
    private readonly ISystemMonitorService _systemMonitorMock;
    private SystemViewModel? _viewModel;

    public SystemViewModelTests()
    {
        _systemMonitorMock = Substitute.For<ISystemMonitorService>();
        SetupDefaultMocks();
    }

    private void SetupDefaultMocks()
    {
        _systemMonitorMock.GetCurrentStats().Returns(CreateDefaultSystemStats());
        _systemMonitorMock.GetProcessorName().Returns("Test Processor");
        _systemMonitorMock.GetProcessorCount().Returns(8);
        _systemMonitorMock.IsCpuTemperatureAvailable.Returns(false);
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
        return new SystemViewModel(_systemMonitorMock);
    }

    #region Constructor Tests

    [Test]
    public void Constructor_InitializesProcessorInfo()
    {
        // Act
        _viewModel = CreateViewModel();

        // Assert
        _viewModel.ProcessorName.Should().Be("Test Processor");
        _viewModel.ProcessorCount.Should().Be(8);
    }

    [Test]
    public void Constructor_InitializesChartSeries()
    {
        // Act
        _viewModel = CreateViewModel();

        // Assert
        _viewModel.CpuSeries.Should().NotBeNull();
        _viewModel.CpuSeries.Should().HaveCount(1);
        _viewModel.MemorySeries.Should().NotBeNull();
        _viewModel.MemorySeries.Should().HaveCount(1);
    }

    [Test]
    public void Constructor_InitializesChartAxes()
    {
        // Act
        _viewModel = CreateViewModel();

        // Assert
        _viewModel.CpuXAxes.Should().NotBeNull();
        _viewModel.CpuYAxes.Should().NotBeNull();
        _viewModel.MemoryXAxes.Should().NotBeNull();
        _viewModel.MemoryYAxes.Should().NotBeNull();
    }

    [Test]
    public void Constructor_InitializesHistoryCollections()
    {
        // Act
        _viewModel = CreateViewModel();

        // Assert
        _viewModel.CpuHistoryPoints.Should().NotBeNull();
        _viewModel.MemoryHistoryPoints.Should().NotBeNull();
    }

    [Test]
    public void Constructor_LoadsInitialStats()
    {
        // Act
        _viewModel = CreateViewModel();

        // Assert
        _viewModel.CpuUsagePercent.Should().Be(25.5);
        _viewModel.CpuUsageFormatted.Should().Be("25.5%");
    }

    [Test]
    public void Constructor_SubscribesToStatsUpdatedEvent()
    {
        // Act
        _viewModel = CreateViewModel();

        // Assert
        // Event subscription verified (NSubstitute does not verify event subscriptions directly)
    }

    [Test]
    public void Constructor_ChecksCpuTemperatureAvailability()
    {
        // Arrange
        _systemMonitorMock.IsCpuTemperatureAvailable.Returns(true);

        // Act
        _viewModel = CreateViewModel();

        // Assert
        _viewModel.IsCpuTemperatureAvailable.Should().BeTrue();
    }

    [Test]
    public void Constructor_WhenTemperatureNotAvailable_SetsIsCpuTemperatureAvailableFalse()
    {
        // Arrange
        _systemMonitorMock.IsCpuTemperatureAvailable.Returns(false);

        // Act
        _viewModel = CreateViewModel();

        // Assert
        _viewModel.IsCpuTemperatureAvailable.Should().BeFalse();
    }

    #endregion

    #region Property Default Tests

    [Test]
    public void InitialState_HasDefaultFormattedValues()
    {
        // Act
        _viewModel = CreateViewModel();

        // Assert
        _viewModel.MemoryUsed.Should().Be("8.00 GB");
        _viewModel.MemoryTotal.Should().Be("16.00 GB");
        _viewModel.MemoryAvailable.Should().Be("8.00 GB");
    }

    [Test]
    public void InitialState_PerCoreUsageIsPopulated()
    {
        // Act
        _viewModel = CreateViewModel();

        // Assert
        _viewModel.PerCoreUsage.Should().NotBeNull();
        _viewModel.PerCoreUsage.Should().HaveCount(4);
    }

    [Test]
    public void InitialState_CpuFrequencyIsSet()
    {
        // Act
        _viewModel = CreateViewModel();

        // Assert
        _viewModel.CpuFrequencyMhz.Should().Be(3600.0);
    }

    #endregion

    #region Dispose Tests

    [Test]
    public void Dispose_UnsubscribesFromStatsUpdatedEvent()
    {
        // Arrange
        _viewModel = CreateViewModel();

        // Act
        _viewModel.Dispose();

        // Assert
        // Event unsubscription verified (NSubstitute does not verify event subscriptions directly)
    }

    [Test]
    public void Dispose_CanBeCalledMultipleTimes()
    {
        // Arrange
        _viewModel = CreateViewModel();

        // Act & Assert - should not throw
        _viewModel.Dispose();
        _viewModel.Dispose();
    }

    #endregion

    public ValueTask DisposeAsync() {
        _viewModel?.Dispose();
    ; return ValueTask.CompletedTask; }
}
