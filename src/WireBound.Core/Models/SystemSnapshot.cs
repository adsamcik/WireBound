using System.ComponentModel.DataAnnotations;

namespace WireBound.Core.Models;

/// <summary>
/// Represents a single system stats measurement snapshot for chart history
/// </summary>
public class SystemSnapshot
{
    [Key]
    public int Id { get; set; }

    /// <summary>
    /// The timestamp of this measurement
    /// </summary>
    public DateTime Timestamp { get; set; }

    /// <summary>
    /// CPU usage percentage (0-100)
    /// </summary>
    public double CpuPercent { get; set; }

    /// <summary>
    /// Memory usage percentage (0-100)
    /// </summary>
    public double MemoryPercent { get; set; }
}
