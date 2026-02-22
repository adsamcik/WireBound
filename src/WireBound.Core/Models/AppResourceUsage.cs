using WireBound.Core.Helpers;

namespace WireBound.Core.Models;

/// <summary>
/// Application-level resource usage — all processes from the same executable merged.
/// Values are smoothed via dual-rate EMA by the service layer.
/// </summary>
public class AppResourceUsage
{
    /// <summary>
    /// Stable identifier (SHA256 of executable path)
    /// </summary>
    public string AppIdentifier { get; set; } = string.Empty;

    /// <summary>
    /// Friendly display name for the application
    /// </summary>
    public string AppName { get; set; } = string.Empty;

    /// <summary>
    /// Full path to the executable
    /// </summary>
    public string ExecutablePath { get; set; } = string.Empty;

    /// <summary>
    /// Category this application belongs to (e.g., "Web Browsers")
    /// </summary>
    public string CategoryName { get; set; } = string.Empty;

    /// <summary>
    /// Smoothed private (committed) memory in bytes
    /// </summary>
    public long PrivateBytes { get; set; }

    /// <summary>
    /// Smoothed CPU usage percentage (0-100, can exceed 100 on multi-core)
    /// </summary>
    public double CpuPercent { get; set; }

    /// <summary>
    /// Number of processes belonging to this application
    /// </summary>
    public int ProcessCount { get; set; }

    /// <summary>
    /// Formatted private bytes string (e.g., "2.3 GB")
    /// </summary>
    public string PrivateBytesFormatted => ByteFormatter.FormatBytes(PrivateBytes);

    /// <summary>
    /// Formatted CPU percentage string (e.g., "8.5%")
    /// </summary>
    public string CpuPercentFormatted => $"{CpuPercent:F1}%";
}
