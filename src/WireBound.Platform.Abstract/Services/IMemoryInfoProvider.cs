using WireBound.Platform.Abstract.Models;

namespace WireBound.Platform.Abstract.Services;

/// <summary>
/// Platform-specific provider for memory (RAM) information
/// </summary>
public interface IMemoryInfoProvider
{
    /// <summary>
    /// Get current memory usage statistics
    /// </summary>
    MemoryInfoData GetMemoryInfo();

    /// <summary>
    /// Get total physical memory in bytes
    /// </summary>
    long GetTotalPhysicalMemory();

    /// <summary>
    /// Whether virtual memory stats are available
    /// </summary>
    bool SupportsVirtualMemory { get; }
}
