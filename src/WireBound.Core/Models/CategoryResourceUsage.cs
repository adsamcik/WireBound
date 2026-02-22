using WireBound.Core.Helpers;

namespace WireBound.Core.Models;

/// <summary>
/// Category-level resource usage — all applications in a category summed.
/// </summary>
public class CategoryResourceUsage
{
    /// <summary>
    /// Category name (e.g., "Web Browsers", "Development Tools")
    /// </summary>
    public string CategoryName { get; set; } = string.Empty;

    /// <summary>
    /// Total smoothed private bytes for all apps in this category
    /// </summary>
    public long TotalPrivateBytes { get; set; }

    /// <summary>
    /// Total smoothed CPU percentage for all apps in this category
    /// </summary>
    public double TotalCpuPercent { get; set; }

    /// <summary>
    /// Number of distinct applications in this category
    /// </summary>
    public int AppCount { get; set; }

    /// <summary>
    /// Total number of processes across all apps in this category
    /// </summary>
    public int ProcessCount { get; set; }

    /// <summary>
    /// Formatted total private bytes (e.g., "3.1 GB")
    /// </summary>
    public string TotalPrivateBytesFormatted => ByteFormatter.FormatBytes(TotalPrivateBytes);

    /// <summary>
    /// Formatted total CPU percentage (e.g., "12.3%")
    /// </summary>
    public string TotalCpuPercentFormatted => $"{TotalCpuPercent:F1}%";
}
