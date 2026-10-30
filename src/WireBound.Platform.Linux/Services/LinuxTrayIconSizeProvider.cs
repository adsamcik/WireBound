using System.Runtime.Versioning;
using WireBound.Platform.Abstract.Services;

namespace WireBound.Platform.Linux.Services;

/// <summary>
/// Linux tray hosts do not expose a common API for the icon's physical size.
/// The primary Avalonia screen scale is used as a fallback by the tray service.
/// </summary>
[SupportedOSPlatform("linux")]
public sealed class LinuxTrayIconSizeProvider : ITrayIconSizeProvider
{
    public int? GetPixelSize() => null;
}
