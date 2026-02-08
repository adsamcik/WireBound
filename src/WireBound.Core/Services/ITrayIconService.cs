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
    /// Gets or sets whether the tray icon should show a dynamic activity graph.
    /// When enabled, the icon displays a real-time network activity meter similar to Task Manager.
    /// </summary>
    bool ShowActivityGraph { get; set; }

    /// <summary>
    /// Hides the main application window to the system tray.
    /// </summary>
    void HideMainWindow();

    /// <summary>
    /// Shows the main application window and brings it to the foreground.
    /// </summary>
    void ShowMainWindow();

    /// <summary>
    /// Updates the tray icon with current network activity.
    /// When ShowActivityGraph is enabled, this updates the dynamic activity meter.
    /// </summary>
    /// <param name="downloadSpeedBps">Current download speed in bytes per second</param>
    /// <param name="uploadSpeedBps">Current upload speed in bytes per second</param>
    /// <param name="maxSpeedBps">Maximum speed for scaling (auto-scales if 0)</param>
        void UpdateActivity(long downloadSpeedBps, long uploadSpeedBps, long maxSpeedBps = 0);

    /// <summary>
    /// Sets whether an update is available, adding/removing a tray context menu item.
    /// Pass null version to clear the update item.
    /// </summary>
    void SetUpdateAvailable(string? version, Action? onClicked);
}
