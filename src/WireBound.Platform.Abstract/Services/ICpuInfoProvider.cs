using WireBound.Platform.Abstract.Models;

namespace WireBound.Platform.Abstract.Services;

/// <summary>
/// Platform-specific provider for CPU information
/// </summary>
public interface ICpuInfoProvider
{
    /// <summary>
    /// Get the CPU/processor name
    /// </summary>
    string GetProcessorName();

    /// <summary>
    /// Get the number of logical processors (cores * threads per core)
    /// </summary>
    int GetProcessorCount();

    /// <summary>
    /// Get current CPU usage statistics
    /// </summary>
    CpuInfoData GetCpuInfo();

    /// <summary>
    /// Whether per-core CPU usage monitoring is supported
    /// </summary>
    bool SupportsPerCoreUsage { get; }

    /// <summary>
    /// Whether CPU temperature monitoring is supported
    /// </summary>
    bool SupportsTemperature { get; }

    /// <summary>
    /// Whether CPU frequency monitoring is supported
    /// </summary>
    bool SupportsFrequency { get; }
}
