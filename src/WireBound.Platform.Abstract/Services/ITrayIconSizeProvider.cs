namespace WireBound.Platform.Abstract.Services;

/// <summary>
/// Reports the physical pixel size requested by the system tray's display.
/// Returns null when the platform cannot identify the tray display.
/// </summary>
public interface ITrayIconSizeProvider
{
    int? GetPixelSize();
}
