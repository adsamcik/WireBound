namespace WireBound.Core.Services;

/// <summary>
/// Service interface for system tray icon functionality.
/// </summary>
public interface ITrayIconService : IDisposable
{
    /// <summary>
    /// Hides the main application window to the system tray.
    /// </summary>
    void HideMainWindow();
    
    /// <summary>
    /// Shows the main application window and brings it to the foreground.
    /// </summary>
    void ShowMainWindow();
}
