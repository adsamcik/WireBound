namespace WireBound.Core.Models;

/// <summary>
/// Hourly aggregated system statistics for historical tracking
/// </summary>
public class HourlySystemStats
{
    public int Id { get; set; }
    public DateTime Hour { get; set; }
    
    // CPU metrics
    public double AvgCpuPercent { get; set; }
    public double MaxCpuPercent { get; set; }
    public double MinCpuPercent { get; set; }
    
    // Memory metrics  
    public double AvgMemoryPercent { get; set; }
    public double MaxMemoryPercent { get; set; }
    public long AvgMemoryUsedBytes { get; set; }
    
    // GPU metrics (optional, null if not available)
    public double? AvgGpuPercent { get; set; }
    public double? MaxGpuPercent { get; set; }
    public double? AvgGpuMemoryPercent { get; set; }
}
