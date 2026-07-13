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
    /// What the system tray icon displays (app icon, network traffic, CPU, or RAM).
    /// </summary>
    public TrayIconMode TrayIconMode { get; set; } = TrayIconMode.Traffic;

    /// <summary>
    /// When the tray icon shows network traffic, which adapter to display.
    /// Empty string = follow the app's monitored adapter; a specific adapter Id
    /// pins the tray graph to that interface regardless of the monitored selection.
    /// </summary>
    public string TrayTrafficAdapterId { get; set; } = string.Empty;

    /// <summary>
    /// Whether to start the application minimized to the system tray
    /// </summary>
    public bool StartMinimized { get; set; } = false;

    /// <summary>
    /// Whether to use IP Helper API for more accurate stats (Windows-specific)
    /// </summary>
    public bool UseIpHelperApi { get; set; } = false;

    /// <summary>
    /// Selected adapter ID to monitor.
    /// "auto" = auto-detect primary via default gateway,
    /// empty = aggregate all adapters,
    /// specific ID = monitor that adapter only.
    /// </summary>
    public string SelectedAdapterId { get; set; } = "auto";

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
    /// Whether to auto-start the elevated helper process at system login.
    /// Uses Task Scheduler (Windows) or systemd (Linux) to start without prompts.
    /// </summary>
    public bool StartHelperWithSystem { get; set; } = false;

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

    // === Memory Alerts ===

    /// <summary>
    /// Whether memory pressure alerts are enabled.
    /// When disabled, only ambient indicators (tray color, health strip pulse) are active.
    /// </summary>
    public bool MemoryAlertsEnabled { get; set; } = false;

    /// <summary>
    /// Memory usage percentage threshold for warning state (0-100).
    /// Alert fires when usage exceeds this AND available memory is below the free floor.
    /// </summary>
    public int MemoryWarningThresholdPercent { get; set; } = 85;

    /// <summary>
    /// Memory usage percentage threshold for critical state (0-100).
    /// </summary>
    public int MemoryCriticalThresholdPercent { get; set; } = 95;

    /// <summary>
    /// Minimum free RAM in megabytes below which alerts can fire.
    /// Prevents false alarms on large-memory machines (e.g., 128 GB workstations).
    /// </summary>
    public int MemoryFreeFloorMb { get; set; } = 2048;

    /// <summary>
    /// Cooldown in seconds between consecutive memory alerts.
    /// Prevents alert fatigue from repeated notifications.
    /// </summary>
    public int MemoryAlertCooldownSeconds { get; set; } = 300;

    /// <summary>
    /// Number of consecutive seconds memory must exceed a threshold before alerting.
    /// Prevents false alarms from brief spikes (e.g., compilation bursts).
    /// </summary>
    public int MemoryAlertSustainedSeconds { get; set; } = 30;

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
