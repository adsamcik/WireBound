using WireBound.Platform.Abstract.Models;
using WireBound.Platform.Abstract.Services;

namespace WireBound.Platform.Stub.Services;

/// <summary>
/// Stub implementation of memory info provider for unsupported platforms
/// </summary>
public sealed class StubMemoryInfoProvider : IMemoryInfoProvider
{
    private readonly Random _random = new();
    private readonly long _totalMemory;

    public StubMemoryInfoProvider()
    {
        // Simulate 16 GB total memory
        _totalMemory = 16L * 1024 * 1024 * 1024;
    }

    public MemoryInfoData GetMemoryInfo()
    {
        // Generate realistic-looking random memory usage for development/testing
        var usagePercent = 40 + _random.NextDouble() * 30; // 40-70% usage
        var usedBytes = (long)(_totalMemory * usagePercent / 100);
        var availableBytes = _totalMemory - usedBytes;

        return new MemoryInfoData
        {
            TotalBytes = _totalMemory,
            UsedBytes = usedBytes,
            AvailableBytes = availableBytes,
            TotalVirtualBytes = _totalMemory * 2, // Simulate page file
            UsedVirtualBytes = (long)(_totalMemory * 2 * usagePercent / 100 * 0.3) // Less virtual memory used
        };
    }

    public long GetTotalPhysicalMemory() => _totalMemory;

    public bool SupportsVirtualMemory => true;
}
