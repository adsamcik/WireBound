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

    public SystemViewModelTests()
    {
        _dispatcherMock = Substitute.For<IUiDispatcher>();
        _systemMonitorMock = Substitute.For<ISystemMonitorService>();
        _navigationServiceMock = Substitute.For<INavigationService>();
        SetupDefaultMocks();
    }

    private void SetupDefaultMocks()
    {
        _systemMonitorMock.GetCurrentStats().Returns(CreateDefaultSystemStats());
        _systemMonitorMock.GetProcessorName().Returns("Test Processor");
        _systemMonitorMock.GetProcessorCount().Returns(8);
        _systemMonitorMock.IsCpuTemperatureAvailable.Returns(false);
        _navigationServiceMock.CurrentView.Returns(Routes.System);
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
        var viewModel = new SystemViewModel(_dispatcherMock, _systemMonitorMock, _navigationServiceMock);
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
