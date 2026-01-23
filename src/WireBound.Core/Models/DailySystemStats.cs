namespace WireBound.Core.Models;

/// <summary>
/// Daily aggregated system statistics for long-term historical tracking
/// </summary>
public class DailySystemStats
{
    public int Id { get; set; }
    public DateOnly Date { get; set; }
    
    // CPU metrics
    public double AvgCpuPercent { get; set; }
    public double MaxCpuPercent { get; set; }
    
    // Memory metrics
    public double AvgMemoryPercent { get; set; }
    public double MaxMemoryPercent { get; set; }
    public long PeakMemoryUsedBytes { get; set; }
    
    // GPU metrics (optional)
    public double? AvgGpuPercent { get; set; }
    public double? MaxGpuPercent { get; set; }
}
