using H.NotifyIcon;
using H.NotifyIcon.Core;
using WireBound.Core.Models;
using WireBound.Core.Services;
using System.Drawing;
using System.Drawing.Drawing2D;
using Color = System.Drawing.Color;
using Point = System.Drawing.Point;
using LineCap = System.Drawing.Drawing2D.LineCap;
using Application = Microsoft.Maui.Controls.Application;

namespace WireBound.Windows.Services;

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

public class WindowsTrayIconService : ITrayIconService
{
    private readonly TrayIconWithContextMenu _trayIcon;
    private readonly INetworkMonitorService _networkMonitor;
    private bool _disposed;
    
    private const double ActivityThresholdBps = 1024;
    
    private readonly Dictionary<NetworkActivityState, IntPtr> _iconCache = new();
    private NetworkActivityState _currentState = NetworkActivityState.Idle;
    
    private static readonly Color PrimaryColor = Color.FromArgb(0, 212, 255);
    private static readonly Color DownloadColor = Color.FromArgb(0, 255, 136);
    private static readonly Color UploadColor = Color.FromArgb(255, 107, 107);
    private static readonly Color BothColor = Color.FromArgb(255, 182, 39);

    public WindowsTrayIconService(INetworkMonitorService networkMonitor)
    {
        _networkMonitor = networkMonitor;
        
        CreateIconCache();
        
        _trayIcon = new TrayIconWithContextMenu
        {
            ToolTip = "WireBound - Network Monitor",
            Icon = _iconCache[NetworkActivityState.Idle],
        };
        
        _trayIcon.ContextMenu = new PopupMenu
        {
            Items =
            {
                new PopupMenuItem("Show WireBound", (_, _) => ShowMainWindow()),
                new PopupMenuSeparator(),
                new PopupMenuItem("Exit", (_, _) => ExitApplication())
            }
        };

        _trayIcon.MessageWindow.MouseEventReceived += (_, e) =>
        {
            if (e.MouseEvent == MouseEvent.IconLeftDoubleClick)
            {
                ShowMainWindow();
            }
        };
        
        _trayIcon.Create();
        _networkMonitor.StatsUpdated += OnStatsUpdated;
    }

    private void CreateIconCache()
    {
        foreach (NetworkActivityState state in Enum.GetValues<NetworkActivityState>())
        {
            using var bitmap = CreateIconBitmap(state);
            var iconHandle = bitmap.GetHicon();
            _iconCache[state] = iconHandle;
        }
    }

    private Bitmap CreateIconBitmap(NetworkActivityState state)
    {
        var bitmap = new Bitmap(32, 32);
        using var g = Graphics.FromImage(bitmap);
        g.SmoothingMode = SmoothingMode.AntiAlias;
        
        var ringColor = state switch
        {
            NetworkActivityState.Both => BothColor,
            NetworkActivityState.Downloading => DownloadColor,
            NetworkActivityState.Uploading => UploadColor,
            _ => PrimaryColor
        };
        
        var ringThickness = state == NetworkActivityState.Idle ? 2f : 3f;
        using var ringPen = new Pen(ringColor, ringThickness);
        g.DrawEllipse(ringPen, 2, 2, 27, 27);
        
        using var centerBrush = new SolidBrush(Color.FromArgb(40, ringColor));
        g.FillEllipse(centerBrush, 4, 4, 23, 23);
        
        if (state.HasFlag(NetworkActivityState.Downloading))
        {
            DrawDownArrow(g, state == NetworkActivityState.Both ? 10 : 16);
        }
        
        if (state.HasFlag(NetworkActivityState.Uploading))
        {
            DrawUpArrow(g, state == NetworkActivityState.Both ? 22 : 16);
        }
        
        if (state == NetworkActivityState.Idle)
        {
            DrawIdleIcon(g);
        }
        
        return bitmap;
    }
    
    private void DrawDownArrow(Graphics g, int centerX)
    {
        using var pen = new Pen(DownloadColor, 2f) { StartCap = LineCap.Round, EndCap = LineCap.Round };
        using var brush = new SolidBrush(DownloadColor);
        
        g.DrawLine(pen, centerX, 9, centerX, 20);
        
        var arrowPoints = new Point[]
        {
            new(centerX - 4, 17),
            new(centerX, 23),
            new(centerX + 4, 17)
        };
        g.FillPolygon(brush, arrowPoints);
    }
    
    private void DrawUpArrow(Graphics g, int centerX)
    {
        using var pen = new Pen(UploadColor, 2f) { StartCap = LineCap.Round, EndCap = LineCap.Round };
        using var brush = new SolidBrush(UploadColor);
        
        g.DrawLine(pen, centerX, 23, centerX, 12);
        
        var arrowPoints = new Point[]
        {
            new(centerX - 4, 15),
            new(centerX, 9),
            new(centerX + 4, 15)
        };
        g.FillPolygon(brush, arrowPoints);
    }
    
    private void DrawIdleIcon(Graphics g)
    {
        using var pen = new Pen(Color.White, 1.5f);
        g.DrawLine(pen, 10, 16, 22, 16);
        g.DrawLine(pen, 16, 10, 16, 22);
        g.DrawEllipse(pen, 11, 11, 10, 10);
    }

    private void OnStatsUpdated(object? sender, NetworkStats stats)
    {
        if (_disposed) return;
        
        var newState = NetworkActivityState.Idle;
        
        if (stats.DownloadSpeedBps >= ActivityThresholdBps)
            newState |= NetworkActivityState.Downloading;
        
        if (stats.UploadSpeedBps >= ActivityThresholdBps)
            newState |= NetworkActivityState.Uploading;
        
        if (newState != _currentState)
        {
            _currentState = newState;
            _trayIcon.UpdateIcon(_iconCache[newState]);
        }
        
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
                    WindowsPInvoke.ShowWindow(hwnd, WindowsPInvoke.SW_RESTORE);
                    WindowsPInvoke.SetForegroundWindow(hwnd);
                }
            }
        });
    }

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
                    WindowsPInvoke.ShowWindow(hwnd, WindowsPInvoke.SW_HIDE);
                }
            }
        });
    }

    private void ExitApplication()
    {
        MainThread.BeginInvokeOnMainThread(() =>
        {
            Dispose();
            Application.Current?.Quit();
        });
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        
        _networkMonitor.StatsUpdated -= OnStatsUpdated;
        _trayIcon.Dispose();
        
        foreach (var iconHandle in _iconCache.Values)
        {
            WindowsPInvoke.DestroyIcon(iconHandle);
        }
        _iconCache.Clear();
    }
}

internal static partial class WindowsPInvoke
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
