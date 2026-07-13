using WireBound.Core.Models;

namespace WireBound.Core.Services;

/// <summary>
/// Service interface for system tray icon functionality.
/// </summary>
public interface ITrayIconService : IDisposable
{
    /// <summary>
    /// Gets or sets whether the application should minimize to system tray.
    /// </summary>
    bool MinimizeToTray { get; set; }

    /// <summary>
    /// Gets or sets what the tray icon displays (app icon, network traffic, CPU, or RAM).
    /// Changing this re-renders the icon immediately.
    /// </summary>
    TrayIconMode IconMode { get; set; }

    /// <summary>
    /// Gets or sets which network adapter's traffic the tray shows when
    /// <see cref="IconMode"/> is <see cref="TrayIconMode.Traffic"/>.
    /// Empty string = follow the app's monitored adapter; a specific adapter Id
    /// pins the tray graph to that interface. Consumed by the polling service.
    /// </summary>
    string TrafficAdapterId { get; set; }

    /// <summary>
    /// Hides the main application window to the system tray.
    /// </summary>
    void HideMainWindow();

    /// <summary>
    /// Shows the main application window and brings it to the foreground.
    /// </summary>
    void ShowMainWindow();

    /// <summary>
    /// Updates the tray icon with the latest network and system metrics.
    /// Renders at most one icon per call based on the active <see cref="IconMode"/>,
    /// and refreshes the tooltip. Safe to call from any thread.
    /// </summary>
    /// <param name="downloadSpeedBps">Download speed in bytes per second for the tray's traffic source</param>
    /// <param name="uploadSpeedBps">Upload speed in bytes per second for the tray's traffic source</param>
    /// <param name="cpuPercent">Current CPU usage percentage (0-100)</param>
    /// <param name="ramPercent">Current memory usage percentage (0-100)</param>
    void UpdateMetrics(long downloadSpeedBps, long uploadSpeedBps, double cpuPercent, double ramPercent);

    /// <summary>
    /// Sets whether an update is available, adding/removing a tray context menu item.
    /// Pass null version to clear the update item.
    /// </summary>
    void SetUpdateAvailable(string? version, Action? onClicked);

    /// <summary>
    /// Updates the tray icon background tint and tooltip to reflect current memory pressure.
    /// Safe to call from any thread.
    /// </summary>
    void UpdateMemoryPressure(MemoryPressureLevel level, double usagePercent, long availableBytes, long swapUsedBytes);
}
