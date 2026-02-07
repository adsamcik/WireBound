using Microsoft.Extensions.Logging;
using WireBound.Avalonia.Services;
using WireBound.Core.Models;
using WireBound.Platform.Abstract.Models;
using WireBound.Platform.Abstract.Services;

namespace WireBound.Tests.Services;

/// <summary>
/// Unit tests for SystemMonitorService
/// </summary>
public class SystemMonitorServiceTests : IDisposable
{
    private readonly ICpuInfoProvider _cpuProvider;
    private readonly IMemoryInfoProvider _memoryProvider;
    private readonly ILogger<SystemMonitorService> _logger;
    private readonly SystemMonitorService _service;

    public SystemMonitorServiceTests()
    {
        _cpuProvider = Substitute.For<ICpuInfoProvider>();
        _cpuProvider.GetProcessorName().Returns("Test CPU");
        _cpuProvider.GetProcessorCount().Returns(8);
        _cpuProvider.GetCpuInfo().Returns(new CpuInfoData
        {
            UsagePercent = 50,
            ProcessorCount = 8
        });
        _cpuProvider.SupportsTemperature.Returns(true);
        _cpuProvider.SupportsPerCoreUsage.Returns(true);

        _memoryProvider = Substitute.For<IMemoryInfoProvider>();
        _memoryProvider.GetTotalPhysicalMemory().Returns(16_000_000_000L);
        _memoryProvider.GetMemoryInfo().Returns(new MemoryInfoData
        {
            TotalBytes = 16_000_000_000,
            UsedBytes = 8_000_000_000,
            AvailableBytes = 8_000_000_000
        });

        _logger = Substitute.For<ILogger<SystemMonitorService>>();

        _service = new SystemMonitorService(_cpuProvider, _memoryProvider, _logger);
    }

    public void Dispose()
    {
        _service.Dispose();
        GC.SuppressFinalize(this);
    }

    // ═══════════════════════════════════════════════════════════════════════
    // Constructor Tests
    // ═══════════════════════════════════════════════════════════════════════

    [Test]
    public void Constructor_CallsGetProcessorName()
    {
        // Assert - constructor already called in setup
        _cpuProvider.Received(1).GetProcessorName();
    }

    // ═══════════════════════════════════════════════════════════════════════
    // Poll Tests
    // ═══════════════════════════════════════════════════════════════════════

    [Test]
    public void Poll_UpdatesStatsFromProviders()
    {
        // Act
        _service.Poll();

        // Assert
        var stats = _service.GetCurrentStats();
        stats.Cpu.UsagePercent.Should().Be(50);
        stats.Cpu.ProcessorCount.Should().Be(8);
        stats.Memory.TotalBytes.Should().Be(16_000_000_000);
        stats.Memory.UsedBytes.Should().Be(8_000_000_000);
        stats.Memory.AvailableBytes.Should().Be(8_000_000_000);
    }

    [Test]
    public void Poll_FiresStatsUpdatedEvent()
    {
        // Arrange
        SystemStats? receivedStats = null;
        _service.StatsUpdated += (_, stats) => receivedStats = stats;

        // Act
        _service.Poll();

        // Assert
        receivedStats.Should().NotBeNull();
        receivedStats!.Cpu.UsagePercent.Should().Be(50);
    }

    // ═══════════════════════════════════════════════════════════════════════
    // GetCurrentStats Tests
    // ═══════════════════════════════════════════════════════════════════════

    [Test]
    public void GetCurrentStats_BeforeFirstPoll_ReturnsDefault()
    {
        // Act
        var stats = _service.GetCurrentStats();

        // Assert - should be default SystemStats
        stats.Should().NotBeNull();
        stats.Cpu.Should().NotBeNull();
        stats.Memory.Should().NotBeNull();
    }

    // ═══════════════════════════════════════════════════════════════════════
    // Delegation Tests
    // ═══════════════════════════════════════════════════════════════════════

    [Test]
    public void GetProcessorName_DelegatesToProvider()
    {
        // Act
        var name = _service.GetProcessorName();

        // Assert
        name.Should().Be("Test CPU");
    }

    [Test]
    public void GetProcessorCount_DelegatesToProvider()
    {
        // Act
        var count = _service.GetProcessorCount();

        // Assert
        count.Should().Be(8);
    }

    [Test]
    public void IsCpuTemperatureAvailable_DelegatesToProvider()
    {
        // Act
        var available = _service.IsCpuTemperatureAvailable;

        // Assert
        available.Should().BeTrue();
    }

    [Test]
    public void IsPerCoreUsageAvailable_DelegatesToProvider()
    {
        // Act
        var available = _service.IsPerCoreUsageAvailable;

        // Assert
        available.Should().BeTrue();
    }

    // ═══════════════════════════════════════════════════════════════════════
    // Dispose Tests
    // ═══════════════════════════════════════════════════════════════════════

    [Test]
    public void Dispose_DisposesProvidersImplementingIDisposable()
    {
        // Arrange - create providers that implement IDisposable
        var disposableCpu = Substitute.For<ICpuInfoProvider, IDisposable>();
        disposableCpu.GetProcessorName().Returns("Disposable CPU");
        disposableCpu.GetProcessorCount().Returns(4);

        var disposableMemory = Substitute.For<IMemoryInfoProvider, IDisposable>();
        disposableMemory.GetTotalPhysicalMemory().Returns(8_000_000_000L);

        var svc = new SystemMonitorService(disposableCpu, disposableMemory);

        // Act
        svc.Dispose();

        // Assert
        ((IDisposable)disposableCpu).Received(1).Dispose();
        ((IDisposable)disposableMemory).Received(1).Dispose();
    }

    [Test]
    public void Poll_AfterDispose_ThrowsObjectDisposedException()
    {
        // Arrange
        var cpuProvider = Substitute.For<ICpuInfoProvider>();
        cpuProvider.GetProcessorName().Returns("CPU");
        cpuProvider.GetProcessorCount().Returns(4);
        var memProvider = Substitute.For<IMemoryInfoProvider>();
        memProvider.GetTotalPhysicalMemory().Returns(8_000_000_000L);

        var svc = new SystemMonitorService(cpuProvider, memProvider);
        svc.Dispose();

        // Act & Assert
        var action = () => svc.Poll();
        action.Should().Throw<ObjectDisposedException>();
    }
}
