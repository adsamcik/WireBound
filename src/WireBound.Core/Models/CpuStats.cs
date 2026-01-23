namespace WireBound.Core.Models;

/// <summary>
/// CPU statistics snapshot
/// </summary>
public class CpuStats
{
    /// <summary>
    /// Timestamp when stats were captured
    /// </summary>
    public DateTime Timestamp { get; set; } = DateTime.Now;

    /// <summary>
    /// Overall CPU usage percentage (0-100)
    /// </summary>
    public double UsagePercent { get; set; }

    /// <summary>
    /// Per-core CPU usage percentages (0-100 each)
    /// </summary>
    public double[] PerCoreUsagePercent { get; set; } = [];

    /// <summary>
    /// Number of logical processors (cores/threads)
    /// </summary>
    public int ProcessorCount { get; set; }

    /// <summary>
    /// CPU name/model if available
    /// </summary>
    public string ProcessorName { get; set; } = string.Empty;

    /// <summary>
    /// Current CPU frequency in MHz (if available)
    /// </summary>
    public double? FrequencyMhz { get; set; }

    /// <summary>
    /// CPU temperature in Celsius (if available, requires platform-specific access)
    /// </summary>
    public double? TemperatureCelsius { get; set; }
}
