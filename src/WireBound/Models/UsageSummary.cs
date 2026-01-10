namespace WireBound.Models;

/// <summary>
/// Summary statistics for a time period
/// </summary>
public class UsageSummary
{
    /// <summary>
    /// Start of the summary period
    /// </summary>
    public DateOnly PeriodStart { get; set; }
    
    /// <summary>
    /// End of the summary period
    /// </summary>
    public DateOnly PeriodEnd { get; set; }
    
    /// <summary>
    /// Type of summary (Hourly, Daily, Weekly, Monthly, Yearly)
    /// </summary>
    public SummaryPeriod PeriodType { get; set; }
    
    /// <summary>
    /// Total bytes received
    /// </summary>
    public long TotalReceived { get; set; }
    
    /// <summary>
    /// Total bytes sent
    /// </summary>
    public long TotalSent { get; set; }
    
    /// <summary>
    /// Total bytes (received + sent)
    /// </summary>
    public long TotalBytes => TotalReceived + TotalSent;
    
    /// <summary>
    /// Average bytes per day
    /// </summary>
    public long AverageDailyBytes { get; set; }
    
    /// <summary>
    /// Peak download speed recorded
    /// </summary>
    public long PeakDownloadSpeed { get; set; }
    
    /// <summary>
    /// Peak upload speed recorded
    /// </summary>
    public long PeakUploadSpeed { get; set; }
    
    /// <summary>
    /// Day with highest usage
    /// </summary>
    public DateOnly? PeakUsageDate { get; set; }
    
    /// <summary>
    /// Bytes on peak usage day
    /// </summary>
    public long PeakUsageDayBytes { get; set; }
    
    /// <summary>
    /// Number of active days
    /// </summary>
    public int ActiveDays { get; set; }
    
    /// <summary>
    /// Comparison to previous period (percentage change)
    /// </summary>
    public double ChangeFromPreviousPeriod { get; set; }
    
    /// <summary>
    /// Most active hour of day (0-23)
    /// </summary>
    public int MostActiveHour { get; set; }
    
    /// <summary>
    /// Most active day of week (0=Sunday, 6=Saturday)
    /// </summary>
    public DayOfWeek MostActiveDay { get; set; }
}

/// <summary>
/// Types of summary periods
/// </summary>
public enum SummaryPeriod
{
    Hourly,
    Daily,
    Weekly,
    Monthly,
    Yearly
}
