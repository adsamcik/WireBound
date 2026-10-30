using System.Runtime.InteropServices;
using System.Runtime.Versioning;
using WireBound.Platform.Abstract.Services;

namespace WireBound.Platform.Windows.Services;

/// <summary>
/// Matches Avalonia's Win32 tray implementation: it selects the taskbar monitor
/// and requests a 16 DIP icon at that monitor's effective DPI.
/// </summary>
[SupportedOSPlatform("windows")]
public sealed class WindowsTrayIconSizeProvider : ITrayIconSizeProvider
{
    private const uint AbmGetTaskbarPos = 0x00000005;
    private const uint MonitorDefaultToPrimary = 1;
    private const int MdtEffectiveDpi = 0;

    public int? GetPixelSize()
    {
        var taskbar = new AppBarData { Size = (uint)Marshal.SizeOf<AppBarData>() };
        if (SHAppBarMessage(AbmGetTaskbarPos, ref taskbar) == IntPtr.Zero)
            return null;

        var monitor = MonitorFromPoint(new Point(taskbar.Bounds.Left, taskbar.Bounds.Top), MonitorDefaultToPrimary);
        if (monitor == IntPtr.Zero || GetDpiForMonitor(monitor, MdtEffectiveDpi, out var dpiX, out _) != 0)
            return null;

        return GetPixelSizeForDpi(dpiX);
    }

    internal static int GetPixelSizeForDpi(uint dpi) => (int)Math.Ceiling(16 * dpi / 96.0);

    [StructLayout(LayoutKind.Sequential)]
    private struct Point(int x, int y)
    {
        public int X = x;
        public int Y = y;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct Rect
    {
        public int Left;
        public int Top;
        public int Right;
        public int Bottom;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct AppBarData
    {
        public uint Size;
        public IntPtr Window;
        public uint CallbackMessage;
        public uint Edge;
        public Rect Bounds;
        public IntPtr Parameter;
    }

    [DllImport("shell32.dll")]
    private static extern IntPtr SHAppBarMessage(uint message, ref AppBarData data);

    [DllImport("user32.dll")]
    private static extern IntPtr MonitorFromPoint(Point point, uint flags);

    [DllImport("shcore.dll")]
    private static extern int GetDpiForMonitor(IntPtr monitor, int dpiType, out uint dpiX, out uint dpiY);
}
