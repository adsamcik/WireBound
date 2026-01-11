using H.NotifyIcon;
using H.NotifyIcon.Core;
using WireBound.Services;
using WireBound.Models;
using System.Drawing;
using System.Drawing.Drawing2D;
using Color = System.Drawing.Color;
using Point = System.Drawing.Point;
using LineCap = System.Drawing.Drawing2D.LineCap;
using Application = Microsoft.Maui.Controls.Application;

namespace WireBound.Platforms.Windows;

/// <summary>
/// Represents the current network activity state for icon display.
/// </summary>
[Flags]
public enum NetworkActivityState
{
    Idle = 0,
    Downloading = 1,
    Uploading = 2,
    Both = Downloading | Uploading
}

public class TrayIconService : ITrayIconService
{
    private readonly TrayIconWithContextMenu _trayIcon;
    private readonly INetworkMonitorService _networkMonitor;
    private bool _disposed;
    
    // Activity threshold in bytes per second (1 KB/s minimum to show activity)
    private const double ActivityThresholdBps = 1024;
    
    // Cache for icon handles to avoid recreating icons constantly
    private readonly Dictionary<NetworkActivityState, IntPtr> _iconCache = new();
    private NetworkActivityState _currentState = NetworkActivityState.Idle;
    
    // Colors for activity indicators
    private static readonly Color PrimaryColor = Color.FromArgb(0, 212, 255);      // Cyan
    private static readonly Color DownloadColor = Color.FromArgb(0, 255, 136);     // Green
    private static readonly Color UploadColor = Color.FromArgb(255, 107, 107);     // Red
    private static readonly Color BothColor = Color.FromArgb(255, 182, 39);        // Orange/Yellow

    public TrayIconService(INetworkMonitorService networkMonitor)
    {
        _networkMonitor = networkMonitor;
        
        // Pre-create all icon states
        CreateIconCache();
        
        _trayIcon = new TrayIconWithContextMenu
        {
            ToolTip = "WireBound - Network Monitor",
            Icon = _iconCache[NetworkActivityState.Idle],
        };
        
        // Add context menu items
        _trayIcon.ContextMenu = new PopupMenu
        {
            Items =
            {
                new PopupMenuItem("Show WireBound", (_, _) => ShowMainWindow()),
                new PopupMenuSeparator(),
                new PopupMenuItem("Exit", (_, _) => ExitApplication())
            }
        };

        // Handle double-click
        _trayIcon.MessageWindow.MouseEventReceived += (_, e) =>
        {
            if (e.MouseEvent == MouseEvent.IconLeftDoubleClick)
            {
                ShowMainWindow();
            }
        };
        
        // Create the icon in system tray
        _trayIcon.Create();
        
        // Subscribe to network updates for tooltip
        _networkMonitor.StatsUpdated += OnStatsUpdated;
    }

    /// <summary>
    /// Pre-creates icons for all activity states to avoid GDI overhead during updates.
    /// </summary>
    private void CreateIconCache()
    {
        foreach (NetworkActivityState state in Enum.GetValues<NetworkActivityState>())
        {
            using var bitmap = CreateIconBitmap(state);
            var iconHandle = bitmap.GetHicon();
            _iconCache[state] = iconHandle;
        }
    }

    /// <summary>
    /// Creates an icon bitmap for the specified activity state.
    /// </summary>
    private Bitmap CreateIconBitmap(NetworkActivityState state)
    {
        var bitmap = new Bitmap(32, 32);
        using var g = Graphics.FromImage(bitmap);
        g.SmoothingMode = SmoothingMode.AntiAlias;
        
        // Determine the ring color based on activity state
        var ringColor = state switch
        {
            NetworkActivityState.Both => BothColor,
            NetworkActivityState.Downloading => DownloadColor,
            NetworkActivityState.Uploading => UploadColor,
            _ => PrimaryColor
        };
        
        // Draw outer activity ring (thicker when active)
        var ringThickness = state == NetworkActivityState.Idle ? 2f : 3f;
        using var ringPen = new Pen(ringColor, ringThickness);
        g.DrawEllipse(ringPen, 2, 2, 27, 27);
        
        // Draw center fill (slightly transparent)
        using var centerBrush = new SolidBrush(Color.FromArgb(40, ringColor));
        g.FillEllipse(centerBrush, 4, 4, 23, 23);
        
        // Draw activity arrows based on state
        if (state.HasFlag(NetworkActivityState.Downloading))
        {
            DrawDownArrow(g, state == NetworkActivityState.Both ? 10 : 16);
        }
        
        if (state.HasFlag(NetworkActivityState.Uploading))
        {
            DrawUpArrow(g, state == NetworkActivityState.Both ? 22 : 16);
        }
        
        // If idle, draw a simple network icon
        if (state == NetworkActivityState.Idle)
        {
            DrawIdleIcon(g);
        }
        
        return bitmap;
    }
    
    /// <summary>
    /// Draws a downward arrow for download activity.
    /// </summary>
    private void DrawDownArrow(Graphics g, int centerX)
    {
        using var pen = new Pen(DownloadColor, 2f) { StartCap = LineCap.Round, EndCap = LineCap.Round };
        using var brush = new SolidBrush(DownloadColor);
        
        // Vertical line
        g.DrawLine(pen, centerX, 9, centerX, 20);
        
        // Arrow head
        var arrowPoints = new Point[]
        {
            new(centerX - 4, 17),
            new(centerX, 23),
            new(centerX + 4, 17)
        };
        g.FillPolygon(brush, arrowPoints);
    }
    
    /// <summary>
    /// Draws an upward arrow for upload activity.
    /// </summary>
    private void DrawUpArrow(Graphics g, int centerX)
    {
        using var pen = new Pen(UploadColor, 2f) { StartCap = LineCap.Round, EndCap = LineCap.Round };
        using var brush = new SolidBrush(UploadColor);
        
        // Vertical line
        g.DrawLine(pen, centerX, 23, centerX, 12);
        
        // Arrow head
        var arrowPoints = new Point[]
        {
            new(centerX - 4, 15),
            new(centerX, 9),
            new(centerX + 4, 15)
        };
        g.FillPolygon(brush, arrowPoints);
    }
    
    /// <summary>
    /// Draws a simple idle network icon.
    /// </summary>
    private void DrawIdleIcon(Graphics g)
    {
        using var pen = new Pen(Color.White, 1.5f);
        
        // Draw simplified network/globe pattern
        // Horizontal line
        g.DrawLine(pen, 10, 16, 22, 16);
        // Vertical line
        g.DrawLine(pen, 16, 10, 16, 22);
        // Inner circle
        g.DrawEllipse(pen, 11, 11, 10, 10);
    }

    private void OnStatsUpdated(object? sender, NetworkStats stats)
    {
        if (_disposed) return;
        
        // Determine current activity state
        var newState = NetworkActivityState.Idle;
        
        if (stats.DownloadSpeedBps >= ActivityThresholdBps)
            newState |= NetworkActivityState.Downloading;
        
        if (stats.UploadSpeedBps >= ActivityThresholdBps)
            newState |= NetworkActivityState.Uploading;
        
        // Update icon if state changed
        if (newState != _currentState)
        {
            _currentState = newState;
            _trayIcon.UpdateIcon(_iconCache[newState]);
        }
        
        // Update tooltip with speeds
        var download = FormatSpeed(stats.DownloadSpeedBps);
        var upload = FormatSpeed(stats.UploadSpeedBps);
        _trayIcon.UpdateToolTip($"WireBound\n↓ {download}  ↑ {upload}");
    }

    private static string FormatSpeed(double bytesPerSecond)
    {
        return bytesPerSecond switch
        {
            >= 1_073_741_824 => $"{bytesPerSecond / 1_073_741_824:F2} GB/s",
            >= 1_048_576 => $"{bytesPerSecond / 1_048_576:F2} MB/s",
            >= 1024 => $"{bytesPerSecond / 1024:F2} KB/s",
            _ => $"{bytesPerSecond:F0} B/s"
        };
    }

    /// <summary>
    /// Shows the main application window and brings it to the foreground.
    /// </summary>
    public void ShowMainWindow()
    {
        MainThread.BeginInvokeOnMainThread(() =>
        {
            if (Application.Current?.Windows.Count > 0)
            {
                var window = Application.Current.Windows[0];
                var platformWindow = window.Handler?.PlatformView as Microsoft.UI.Xaml.Window;
                if (platformWindow != null)
                {
                    var hwnd = WinRT.Interop.WindowNative.GetWindowHandle(platformWindow);
                    PInvoke.ShowWindow(hwnd, PInvoke.SW_RESTORE);
                    PInvoke.SetForegroundWindow(hwnd);
                }
            }
        });
    }

    /// <summary>
    /// Hides the main window to the system tray.
    /// </summary>
    public void HideMainWindow()
    {
        MainThread.BeginInvokeOnMainThread(() =>
        {
            if (Application.Current?.Windows.Count > 0)
            {
                var window = Application.Current.Windows[0];
                var platformWindow = window.Handler?.PlatformView as Microsoft.UI.Xaml.Window;
                if (platformWindow != null)
                {
                    var hwnd = WinRT.Interop.WindowNative.GetWindowHandle(platformWindow);
                    PInvoke.ShowWindow(hwnd, PInvoke.SW_HIDE);
                }
            }
        });
    }

    private void ExitApplication()
    {
        MainThread.BeginInvokeOnMainThread(() =>
        {
            Dispose();
            if (Application.Current is App app)
            {
                app.ForceQuit();
            }
            else
            {
                Application.Current?.Quit();
            }
        });
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        
        _networkMonitor.StatsUpdated -= OnStatsUpdated;
        _trayIcon.Dispose();
        
        // Clean up cached icon handles
        foreach (var iconHandle in _iconCache.Values)
        {
            PInvoke.DestroyIcon(iconHandle);
        }
        _iconCache.Clear();
    }
}

/// <summary>
/// P/Invoke declarations using modern LibraryImport source generators for NativeAOT compatibility.
/// LibraryImport generates marshalling code at compile time, eliminating runtime code generation.
/// </summary>
internal static partial class PInvoke
{
    public const int SW_HIDE = 0;
    public const int SW_RESTORE = 9;

    [System.Runtime.InteropServices.LibraryImport("user32.dll")]
    [return: System.Runtime.InteropServices.MarshalAs(System.Runtime.InteropServices.UnmanagedType.Bool)]
    public static partial bool ShowWindow(IntPtr hWnd, int nCmdShow);

    [System.Runtime.InteropServices.LibraryImport("user32.dll")]
    [return: System.Runtime.InteropServices.MarshalAs(System.Runtime.InteropServices.UnmanagedType.Bool)]
    public static partial bool SetForegroundWindow(IntPtr hWnd);
    
    [System.Runtime.InteropServices.LibraryImport("user32.dll")]
    [return: System.Runtime.InteropServices.MarshalAs(System.Runtime.InteropServices.UnmanagedType.Bool)]
    public static partial bool DestroyIcon(IntPtr hIcon);
}
