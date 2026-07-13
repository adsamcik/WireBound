namespace WireBound.Core.Models;

/// <summary>
/// Determines what content the system tray icon displays.
/// </summary>
public enum TrayIconMode
{
    /// <summary>
    /// Static application brand icon (no live metric).
    /// </summary>
    AppIcon = 0,

    /// <summary>
    /// Live network activity graph (download/upload). Default.
    /// </summary>
    Traffic = 1,

    /// <summary>
    /// Live CPU usage graph.
    /// </summary>
    Cpu = 2,

    /// <summary>
    /// Live memory (RAM) usage graph.
    /// </summary>
    Ram = 3,
}
