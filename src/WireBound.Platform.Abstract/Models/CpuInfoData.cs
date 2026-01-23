namespace WireBound.Platform.Abstract.Models;

/// <summary>
/// CPU information data transfer object from platform providers
/// </summary>
public sealed class CpuInfoData
{
    /// <summary>
    /// Overall CPU usage percentage (0-100)
    /// </summary>
    public double UsagePercent { get; init; }

    /// <summary>
    /// Per-core CPU usage percentages (0-100 each)
    /// </summary>
    public double[] PerCoreUsagePercent { get; init; } = [];

    /// <summary>
    /// Number of logical processors
    /// </summary>
    public int ProcessorCount { get; init; }

    /// <summary>
    /// Current CPU frequency in MHz (if available)
    /// </summary>
    public double? FrequencyMhz { get; init; }

    /// <summary>
    /// CPU temperature in Celsius (if available)
    /// </summary>
    public double? TemperatureCelsius { get; init; }
}
