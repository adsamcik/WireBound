using System.ComponentModel.DataAnnotations;

namespace WireBound.Core.Models;

/// <summary>
/// Persisted snapshot of per-application resource usage for historical trending.
/// Aggregated hourly/daily by the background polling service.
/// </summary>
public class ResourceInsightSnapshot
{
    [Key]
    public int Id { get; set; }

    /// <summary>
    /// Timestamp — represents the hour (for Hourly) or date (for Daily)
    /// </summary>
    public DateTime Timestamp { get; set; }

    /// <summary>
    /// Stable identifier (SHA256 of executable path)
    /// </summary>
    public string AppIdentifier { get; set; } = string.Empty;

    /// <summary>
    /// Friendly display name for the application
    /// </summary>
    public string AppName { get; set; } = string.Empty;

    /// <summary>
    /// Category name at the time of recording
    /// </summary>
    public string CategoryName { get; set; } = string.Empty;

    /// <summary>
    /// Average private (committed) memory in bytes during this period
    /// </summary>
    public long PrivateBytes { get; set; }

    /// <summary>
    /// Average working set in bytes during this period
    /// </summary>
    public long WorkingSetBytes { get; set; }

    /// <summary>
    /// Average CPU usage percentage during this period
    /// </summary>
    public double CpuPercent { get; set; }

    /// <summary>
    /// Peak private bytes observed during this period
    /// </summary>
    public long PeakPrivateBytes { get; set; }

    /// <summary>
    /// Peak CPU percentage observed during this period
    /// </summary>
    public double PeakCpuPercent { get; set; }

    /// <summary>
    /// Whether this record is hourly or daily granularity
    /// </summary>
    public UsageGranularity Granularity { get; set; }

    /// <summary>
    /// Last time this record was updated
    /// </summary>
    public DateTime LastUpdated { get; set; }
}
