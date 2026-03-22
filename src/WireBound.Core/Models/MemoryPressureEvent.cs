using System.ComponentModel.DataAnnotations;

namespace WireBound.Core.Models;

/// <summary>
/// Records a memory pressure event for historical analysis.
/// Persisted to SQLite for "why was my machine slow at 3 PM?" queries.
/// </summary>
public class MemoryPressureEvent
{
    [Key]
    public int Id { get; set; }

    /// <summary>
    /// When this pressure event was recorded
    /// </summary>
    public DateTime Timestamp { get; set; }

    /// <summary>
    /// Pressure level at the time of recording
    /// </summary>
    public MemoryPressureLevel Level { get; set; }

    /// <summary>
    /// Physical memory usage percentage (0-100)
    /// </summary>
    public double UsagePercent { get; set; }

    /// <summary>
    /// Available physical memory in bytes at the time of the event
    /// </summary>
    public long AvailableBytes { get; set; }

    /// <summary>
    /// Swap/page file usage in bytes (UsedVirtualBytes - UsedBytes)
    /// </summary>
    public long SwapUsedBytes { get; set; }

    /// <summary>
    /// Top memory-consuming processes at the time (semicolon-delimited, e.g. "chrome:6.2GB;docker:2.1GB")
    /// </summary>
    public string TopProcesses { get; set; } = string.Empty;
}
