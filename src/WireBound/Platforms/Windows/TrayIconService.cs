using H.NotifyIcon;
using H.NotifyIcon.Core;
using WireBound.Services;
using WireBound.Models;
using System.Drawing;
using Color = System.Drawing.Color;
using Application = Microsoft.Maui.Controls.Application;

namespace WireBound.Platforms.Windows;

public class TrayIconService : ITrayIconService
{
    private readonly TrayIconWithContextMenu _trayIcon;
    private readonly INetworkMonitorService _networkMonitor;
    private bool _disposed;

    public TrayIconService(INetworkMonitorService networkMonitor)
    {
        _networkMonitor = networkMonitor;
        
        // Create icon using System.Drawing
        using var bitmap = CreateIconBitmap();
        var iconHandle = bitmap.GetHicon();
        var icon = System.Drawing.Icon.FromHandle(iconHandle);
        
        _trayIcon = new TrayIconWithContextMenu
        {
            ToolTip = "WireBound - Network Monitor",
            Icon = icon.Handle,
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

    private Bitmap CreateIconBitmap()
    {
        // Create a simple 32x32 icon with network-like appearance
        var bitmap = new Bitmap(32, 32);
        using var g = Graphics.FromImage(bitmap);
        
        // Fill with app primary color (cyan)
        using var brush = new SolidBrush(Color.FromArgb(0, 212, 255));
        g.FillEllipse(brush, 2, 2, 28, 28);
        
        // Draw network symbol (simplified globe/arrows)
        using var pen = new Pen(Color.White, 2);
        
        // Horizontal line
        g.DrawLine(pen, 8, 16, 24, 16);
        // Vertical line  
        g.DrawLine(pen, 16, 8, 16, 24);
        // Circle outline
        g.DrawEllipse(pen, 6, 6, 20, 20);
        
        return bitmap;
    }

    private void OnStatsUpdated(object? sender, NetworkStats stats)
    {
        if (_disposed) return;
        
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
}
