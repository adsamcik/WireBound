using Microsoft.Extensions.Logging;
using WireBound.Core.Helpers;
using WireBound.Core.Models;
using WireBound.Core.Services;
using WireBound.Platform.Abstract.Services;

namespace WireBound.Avalonia.Services;

/// <summary>
/// Cross-platform system monitoring service that aggregates CPU and memory data from platform providers
/// </summary>
public sealed class SystemMonitorService : ISystemMonitorService, IDisposable
{
    private readonly ICpuInfoProvider _cpuProvider;
    private readonly IMemoryInfoProvider _memoryProvider;
    private readonly ILogger<SystemMonitorService>? _logger;
    private readonly string _processorName;
    private SystemStats _currentStats = new();
    private bool _disposed;

    public event EventHandler<SystemStats>? StatsUpdated;

    public SystemMonitorService(
        ICpuInfoProvider cpuProvider,
        IMemoryInfoProvider memoryProvider,
        ILogger<SystemMonitorService>? logger = null)
    {
        _cpuProvider = cpuProvider;
        _memoryProvider = memoryProvider;
        _logger = logger;
        _processorName = cpuProvider.GetProcessorName();

        _logger?.LogInformation(
            "SystemMonitorService initialized. CPU: {CpuName} ({CoreCount} cores), RAM: {TotalRam}",
            _processorName,
            GetProcessorCount(),
            ByteFormatter.FormatBytes(_memoryProvider.GetTotalPhysicalMemory()));
    }

    public SystemStats GetCurrentStats() => _currentStats;

    public void Poll()
    {
        ObjectDisposedException.ThrowIf(_disposed, this);

        try
        {
            var cpuData = _cpuProvider.GetCpuInfo();
            var memoryData = _memoryProvider.GetMemoryInfo();

            _currentStats = new SystemStats
            {
                Timestamp = DateTime.Now,
                Cpu = new CpuStats
                {
                    Timestamp = DateTime.Now,
                    UsagePercent = cpuData.UsagePercent,
                    PerCoreUsagePercent = cpuData.PerCoreUsagePercent,
                    ProcessorCount = cpuData.ProcessorCount,
                    ProcessorName = _processorName,
                    FrequencyMhz = cpuData.FrequencyMhz,
                    TemperatureCelsius = cpuData.TemperatureCelsius
                },
                Memory = new MemoryStats
                {
                    Timestamp = DateTime.Now,
                    TotalBytes = memoryData.TotalBytes,
                    UsedBytes = memoryData.UsedBytes,
                    AvailableBytes = memoryData.AvailableBytes,
                    TotalVirtualBytes = memoryData.TotalVirtualBytes,
                    UsedVirtualBytes = memoryData.UsedVirtualBytes
                }
            };

            StatsUpdated?.Invoke(this, _currentStats);
        }
        catch (Exception ex)
        {
            _logger?.LogError(ex, "Error polling system stats");
        }
    }

    public string GetProcessorName() => _processorName;

    public int GetProcessorCount() => _cpuProvider.GetProcessorCount();

    public bool IsCpuTemperatureAvailable => _cpuProvider.SupportsTemperature;

    public bool IsPerCoreUsageAvailable => _cpuProvider.SupportsPerCoreUsage;

    public void Dispose()
    {
        if (_disposed) return;

        if (_cpuProvider is IDisposable disposableCpu)
        {
            disposableCpu.Dispose();
        }

        if (_memoryProvider is IDisposable disposableMemory)
        {
            disposableMemory.Dispose();
        }

        _disposed = true;
    }
}
