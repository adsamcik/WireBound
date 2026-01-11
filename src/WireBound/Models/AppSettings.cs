using System.ComponentModel.DataAnnotations;

namespace WireBound.Models;

/// <summary>
/// Application settings stored in the database
/// </summary>
public class AppSettings
{
    [Key]
    public int Id { get; set; } = 1; // Singleton
    
    /// <summary>
    /// Polling interval in milliseconds
    /// </summary>
    public int PollingIntervalMs { get; set; } = 1000;
    
    /// <summary>
    /// How often to save data to database (in seconds)
    /// </summary>
    public int SaveIntervalSeconds { get; set; } = 60;
    
    /// <summary>
    /// Whether to start with Windows
    /// </summary>
    public bool StartWithWindows { get; set; } = false;
    
    /// <summary>
    /// Whether to minimize to system tray
    /// </summary>
    public bool MinimizeToTray { get; set; } = true;
    
    /// <summary>
    /// Whether to use IP Helper API (fallback method)
    /// </summary>
    public bool UseIpHelperApi { get; set; } = false;
    
    /// <summary>
    /// Selected adapter ID to monitor (empty = all)
    /// </summary>
    public string SelectedAdapterId { get; set; } = string.Empty;
    
    /// <summary>
    /// Number of days to keep historical data
    /// </summary>
    public int DataRetentionDays { get; set; } = 365;
    
    /// <summary>
    /// Theme (Light, Dark, System)
    /// </summary>
    public string Theme { get; set; } = "Dark";
    
    // === Per-App Network Tracking (Optional Feature) ===
    
    /// <summary>
    /// Whether per-application network tracking is enabled.
    /// This feature requires elevated privileges on Windows and triggers UAC when enabled.
    /// </summary>
    public bool IsPerAppTrackingEnabled { get; set; } = false;
    
    /// <summary>
    /// Number of days to retain per-app usage data (0 = indefinite)
    /// </summary>
    public int AppDataRetentionDays { get; set; } = 0;
    
    /// <summary>
    /// Number of days after which hourly data is aggregated to daily.
    /// Helps reduce database size while preserving long-term trends.
    /// </summary>
    public int AppDataAggregateAfterDays { get; set; } = 7;
}
