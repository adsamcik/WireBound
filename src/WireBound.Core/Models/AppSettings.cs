using System.ComponentModel.DataAnnotations;

namespace WireBound.Core.Models;

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
    /// Whether to start with the operating system (Windows: Startup Task or Registry)
    /// </summary>
    public bool StartWithWindows { get; set; } = false;

    /// <summary>
    /// Whether to minimize to system tray
    /// </summary>
    public bool MinimizeToTray { get; set; } = true;

    /// <summary>
    /// Whether to start the application minimized to the system tray
    /// </summary>
    public bool StartMinimized { get; set; } = false;

    /// <summary>
    /// Whether to use IP Helper API for more accurate stats (Windows-specific)
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

    /// <summary>
    /// Speed display unit preference
    /// </summary>
    public SpeedUnit SpeedUnit { get; set; } = SpeedUnit.BytesPerSecond;

    // === Per-App Network Tracking (Optional Feature) ===

    /// <summary>
    /// Whether per-application network tracking is enabled.
    /// This feature requires the elevated helper process to be connected.
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

    // === Dashboard Customization ===

    /// <summary>
    /// Whether to show system metrics (CPU, Memory, GPU) in the dashboard header.
    /// </summary>
    public bool ShowSystemMetricsInHeader { get; set; } = true;

    /// <summary>
    /// Whether to show CPU overlay on the dashboard chart by default.
    /// </summary>
    public bool ShowCpuOverlayByDefault { get; set; } = false;

    /// <summary>
    /// Whether to show memory overlay on the dashboard chart by default.
    /// </summary>
    public bool ShowMemoryOverlayByDefault { get; set; } = false;

    /// <summary>
    /// Whether to show GPU metrics in the system health strip.
    /// </summary>
    public bool ShowGpuMetrics { get; set; } = true;

    /// <summary>
    /// Default time range for the dashboard chart (OneMinute, FiveMinutes, FifteenMinutes, OneHour).
    /// </summary>
    public string DefaultTimeRange { get; set; } = "FiveMinutes";

    // === Performance Mode ===

    /// <summary>
    /// Whether performance mode is enabled (reduces UI update frequency).
    /// </summary>
    public bool PerformanceModeEnabled { get; set; } = false;

    /// <summary>
    /// Chart update interval in milliseconds (500-5000ms).
    /// </summary>
    public int ChartUpdateIntervalMs { get; set; } = 1000;

    // === Insights Page ===

    /// <summary>
    /// Default period for the insights page (Today, ThisWeek, ThisMonth).
    /// </summary>
    public string DefaultInsightsPeriod { get; set; } = "ThisWeek";

    /// <summary>
    /// Whether to show correlation insights between network and system metrics.
    /// </summary>
    public bool ShowCorrelationInsights { get; set; } = true;

    // === Updates ===

    /// <summary>
    /// Whether to check for updates on startup via GitHub Releases API.
    /// </summary>
    public bool CheckForUpdates { get; set; } = true;

    /// <summary>
    /// Whether to automatically download updates in the background after detection.
    /// Restart is always manual. Only applies to Velopack-installed mode.
    /// </summary>
    public bool AutoDownloadUpdates { get; set; } = true;
}
