using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Media.Imaging;
using Avalonia.Threading;
using Serilog;
using SkiaSharp;
using WireBound.Core.Helpers;
using WireBound.Core.Services;

namespace WireBound.Avalonia.Services;

/// <summary>
/// Service for managing the system tray icon functionality.
/// Cross-platform implementation supporting Windows, macOS, and Linux (with AppIndicator).
/// Features a dynamic activity graph similar to Windows Task Manager.
/// </summary>
public sealed class TrayIconService : ITrayIconService
{
    private TrayIcon? _trayIcon;
    private Window? _mainWindow;
    private bool _isDisposed;
    private bool _minimizeToTray;
    private bool _showActivityGraph = true;
    private bool _isTraySupported = true;
    
    // Activity graph data - stores last N readings for the mini-graph
    private const int GraphHistorySize = 16;
    private readonly Queue<(float download, float upload)> _activityHistory = new();
    private long _autoScaleMaxSpeed = 1_000_000; // 1 MB/s default, auto-adjusts
    
    // Icon dimensions
    private const int IconSize = 16;

    /// <summary>
    /// Gets or sets whether the application should minimize to system tray.
    /// </summary>
    public bool MinimizeToTray
    {
        get => _minimizeToTray;
        set
        {
            _minimizeToTray = value;
            UpdateTrayIconVisibility();
        }
    }
    
    /// <summary>
    /// Gets or sets whether the tray icon shows a dynamic activity graph.
    /// </summary>
    public bool ShowActivityGraph
    {
        get => _showActivityGraph;
        set
        {
            _showActivityGraph = value;
            if (_trayIcon != null)
            {
                // When activity graph is enabled, icon should always be visible
                if (value)
                {
                    _trayIcon.IsVisible = true;
                }
                else
                {
                    // Reset to static icon and apply normal visibility rules
                    UpdateStaticIcon();
                    UpdateTrayIconVisibility();
                }
            }
        }
    }

    /// <summary>
    /// Initializes the tray icon service with the main window.
    /// </summary>
    public void Initialize(Window mainWindow, bool minimizeToTray)
    {
        _mainWindow = mainWindow;
        _minimizeToTray = minimizeToTray;
        
        // Check if tray icons are likely to be supported
        _isTraySupported = CheckTraySupport();
        
        if (_isTraySupported)
        {
            SetupTrayIcon();
        }
        else
        {
            Log.Warning("System tray icons may not be fully supported on this platform");
        }
        
        SetupWindowHandlers();
        
        Log.Information("TrayIconService initialized (TraySupported={TraySupported})", _isTraySupported);
    }

    /// <summary>
    /// Checks if system tray icons are likely to be supported on the current platform.
    /// </summary>
    private static bool CheckTraySupport()
    {
        // Windows and macOS generally support tray icons
        if (OperatingSystem.IsWindows() || OperatingSystem.IsMacOS())
        {
            return true;
        }
        
        // Linux support depends on desktop environment (AppIndicator/KDE support)
        // We'll try to create the tray icon anyway and catch any failures
        if (OperatingSystem.IsLinux())
        {
            // Check for common desktop environment indicators
            var desktop = Environment.GetEnvironmentVariable("XDG_CURRENT_DESKTOP") ?? "";
            var sessionType = Environment.GetEnvironmentVariable("XDG_SESSION_TYPE") ?? "";
            
            // Known supported environments
            var supportedDesktops = new[] { "GNOME", "KDE", "XFCE", "Unity", "Cinnamon", "MATE", "Budgie" };
            var isKnownSupported = supportedDesktops.Any(d => 
                desktop.Contains(d, StringComparison.OrdinalIgnoreCase));
            
            if (isKnownSupported)
            {
                Log.Debug("Linux desktop environment detected: {Desktop}, session: {Session}", desktop, sessionType);
            }
            
            // Return true to attempt - we'll handle failures gracefully
            return true;
        }
        
        return true;
    }

    private void SetupTrayIcon()
    {
        if (_trayIcon != null) return;

        try
        {
            var menu = new NativeMenu();
            
            var showItem = new NativeMenuItem("Show WireBound");
            showItem.Click += (_, _) => ShowMainWindow();
            menu.Add(showItem);
            
            menu.Add(new NativeMenuItemSeparator());
            
            var exitItem = new NativeMenuItem("Exit");
            exitItem.Click += (_, _) => ExitApplication();
            menu.Add(exitItem);

            _trayIcon = new TrayIcon
            {
                ToolTipText = "WireBound - Network Monitor",
                Menu = menu,
                IsVisible = true  // Always visible for activity graph
            };

            // Create a programmatic icon
            var icon = CreateIconBitmap();
            if (icon != null)
            {
                _trayIcon.Icon = icon;
            }

            // Click to show window
            _trayIcon.Clicked += (_, _) => ShowMainWindow();
            
            // Register the tray icon with the application
            var trayIcons = TrayIcon.GetIcons(Application.Current!);
            trayIcons?.Add(_trayIcon);
            
            Log.Debug("Tray icon created successfully");
        }
        catch (Exception ex)
        {
            Log.Warning(ex, "Failed to create tray icon - minimize to tray will close to taskbar instead");
            _isTraySupported = false;
            _trayIcon = null;
        }
    }

    /// <summary>
    /// Creates a simple tray icon programmatically using SkiaSharp.
    /// This creates a cyan lightning bolt icon matching the app branding.
    /// </summary>
    private static WindowIcon? CreateIconBitmap()
    {
        return CreateStaticIcon();
    }
    
    /// <summary>
    /// Creates a static lightning bolt icon (used when activity graph is disabled).
    /// </summary>
    private static WindowIcon? CreateStaticIcon()
    {
        try
        {
            using var surface = SKSurface.Create(new SKImageInfo(IconSize, IconSize, SKColorType.Rgba8888, SKAlphaType.Premul));
            var canvas = surface.Canvas;
            canvas.Clear(SKColors.Transparent);
            
            // Draw background circle
            using var bgPaint = new SKPaint
            {
                Color = new SKColor(0, 229, 255), // Cyan (#00E5FF)
                IsAntialias = true,
                Style = SKPaintStyle.Fill
            };
            canvas.DrawCircle(IconSize / 2f, IconSize / 2f, IconSize / 2f - 0.5f, bgPaint);
            
            // Draw lightning bolt (⚡) - simplified path for 16x16
            using var boltPaint = new SKPaint
            {
                Color = SKColors.White,
                IsAntialias = true,
                Style = SKPaintStyle.Fill
            };
            
            using var path = new SKPath();
            // Lightning bolt shape scaled to 16x16
            float scale = IconSize / 32f;
            path.MoveTo(18 * scale, 4 * scale);
            path.LineTo(10 * scale, 16 * scale);
            path.LineTo(14 * scale, 16 * scale);
            path.LineTo(12 * scale, 28 * scale);
            path.LineTo(22 * scale, 14 * scale);
            path.LineTo(18 * scale, 14 * scale);
            path.Close();
            
            canvas.DrawPath(path, boltPaint);
            
            return CreateWindowIconFromSurface(surface);
        }
        catch (Exception ex)
        {
            Log.Warning(ex, "Failed to create static tray icon");
            return null;
        }
    }
    
    /// <summary>
    /// Updates the tray icon to the static version.
    /// </summary>
    private void UpdateStaticIcon()
    {
        if (_trayIcon == null) return;
        
        var icon = CreateStaticIcon();
        if (icon != null)
        {
            _trayIcon.Icon = icon;
        }
    }
    
    /// <summary>
    /// Updates the tray icon with current network activity.
    /// Creates a Task Manager-style activity graph showing download/upload history.
    /// </summary>
    public void UpdateActivity(long downloadSpeedBps, long uploadSpeedBps, long maxSpeedBps = 0)
    {
        if (_trayIcon == null || _isDisposed) return;
        
        // Dispatch to UI thread if not already on it
        if (!Dispatcher.UIThread.CheckAccess())
        {
            Dispatcher.UIThread.Post(() => UpdateActivity(downloadSpeedBps, uploadSpeedBps, maxSpeedBps));
            return;
        }
        
        // Auto-scale: track the maximum speed seen
        var currentMax = Math.Max(downloadSpeedBps, uploadSpeedBps);
        if (currentMax > _autoScaleMaxSpeed)
        {
            _autoScaleMaxSpeed = currentMax;
        }
        else if (_autoScaleMaxSpeed > 1_000_000 && currentMax < _autoScaleMaxSpeed / 4)
        {
            // Slowly decrease the scale if traffic is consistently low
            _autoScaleMaxSpeed = Math.Max(1_000_000, _autoScaleMaxSpeed * 9 / 10);
        }
        
        var effectiveMax = maxSpeedBps > 0 ? maxSpeedBps : _autoScaleMaxSpeed;
        
        // Normalize to 0-1 range
        var downloadNorm = Math.Min(1f, downloadSpeedBps / (float)effectiveMax);
        var uploadNorm = Math.Min(1f, uploadSpeedBps / (float)effectiveMax);
        
        // Add to history
        _activityHistory.Enqueue((downloadNorm, uploadNorm));
        while (_activityHistory.Count > GraphHistorySize)
        {
            _activityHistory.Dequeue();
        }
        
        if (!_showActivityGraph)
        {
            // Just update tooltip with speed info
            UpdateTooltip(downloadSpeedBps, uploadSpeedBps);
            return;
        }
        
        // Create the activity graph icon
        var icon = CreateActivityGraphIcon();
        if (icon != null)
        {
            _trayIcon.Icon = icon;
        }
        
        UpdateTooltip(downloadSpeedBps, uploadSpeedBps);
    }
    
    /// <summary>
    /// Creates a Task Manager-style activity graph icon showing network activity.
    /// The graph shows download (cyan) and upload (magenta) as stacked area bars.
    /// </summary>
    private WindowIcon? CreateActivityGraphIcon()
    {
        try
        {
            using var surface = SKSurface.Create(new SKImageInfo(IconSize, IconSize, SKColorType.Rgba8888, SKAlphaType.Premul));
            var canvas = surface.Canvas;
            
            // Dark background (similar to Task Manager's dark green)
            canvas.Clear(new SKColor(20, 30, 35)); // Dark blue-gray
            
            // Draw border
            using var borderPaint = new SKPaint
            {
                Color = new SKColor(60, 80, 90),
                IsAntialias = false,
                Style = SKPaintStyle.Stroke,
                StrokeWidth = 1
            };
            canvas.DrawRect(0, 0, IconSize - 1, IconSize - 1, borderPaint);
            
            // Download color (cyan - matches app theme)
            using var downloadPaint = new SKPaint
            {
                Color = new SKColor(0, 229, 255), // Cyan
                IsAntialias = false,
                Style = SKPaintStyle.Fill
            };
            
            // Upload color (magenta/pink)
            using var uploadPaint = new SKPaint
            {
                Color = new SKColor(255, 64, 129), // Pink
                IsAntialias = false,
                Style = SKPaintStyle.Fill
            };
            
            // Grid lines (subtle)
            using var gridPaint = new SKPaint
            {
                Color = new SKColor(40, 55, 60),
                IsAntialias = false,
                Style = SKPaintStyle.Stroke,
                StrokeWidth = 1
            };
            
            // Draw horizontal grid lines
            for (int y = 4; y < IconSize - 1; y += 4)
            {
                canvas.DrawLine(1, y, IconSize - 2, y, gridPaint);
            }
            
            // Draw the activity bars
            var history = _activityHistory.ToArray();
            var graphWidth = IconSize - 2; // Leave 1px border on each side
            var graphHeight = IconSize - 2;
            var barWidth = (float)graphWidth / GraphHistorySize;
            
            for (int i = 0; i < history.Length; i++)
            {
                var (download, upload) = history[i];
                var x = 1 + i * barWidth;
                
                // Draw download bar (from bottom up)
                var downloadHeight = download * (graphHeight - 1);
                if (downloadHeight > 0.5f)
                {
                    canvas.DrawRect(
                        x, IconSize - 1 - downloadHeight,
                        Math.Max(1, barWidth - 0.5f), downloadHeight,
                        downloadPaint);
                }
                
                // Draw upload bar (stacked on top of download, or from bottom if no download)
                var uploadHeight = upload * (graphHeight - 1);
                if (uploadHeight > 0.5f)
                {
                    var uploadY = IconSize - 1 - downloadHeight - uploadHeight;
                    canvas.DrawRect(
                        x, Math.Max(1, uploadY),
                        Math.Max(1, barWidth - 0.5f), uploadHeight,
                        uploadPaint);
                }
            }
            
            // If no history, show a flat line at the bottom
            if (history.Length == 0)
            {
                using var emptyPaint = new SKPaint
                {
                    Color = new SKColor(0, 229, 255, 100),
                    IsAntialias = false,
                    Style = SKPaintStyle.Fill
                };
                canvas.DrawRect(1, IconSize - 2, IconSize - 2, 1, emptyPaint);
            }
            
            return CreateWindowIconFromSurface(surface);
        }
        catch (Exception ex)
        {
            Log.Warning(ex, "Failed to create activity graph icon");
            return null;
        }
    }
    
    /// <summary>
    /// Updates the tray icon tooltip with current speed information.
    /// </summary>
    private void UpdateTooltip(long downloadSpeedBps, long uploadSpeedBps)
    {
        if (_trayIcon == null) return;
        
        var downloadSpeed = ByteFormatter.FormatSpeed(downloadSpeedBps);
        var uploadSpeed = ByteFormatter.FormatSpeed(uploadSpeedBps);
        
        _trayIcon.ToolTipText = $"WireBound\n↓ {downloadSpeed}  ↑ {uploadSpeed}";
    }
    
    /// <summary>
    /// Helper to create a WindowIcon from an SKSurface.
    /// </summary>
    private static WindowIcon? CreateWindowIconFromSurface(SKSurface surface)
    {
        using var image = surface.Snapshot();
        using var data = image.Encode(SKEncodedImageFormat.Png, 100);
        using var stream = new MemoryStream(data.ToArray());
        
        var bitmap = new Bitmap(stream);
        return new WindowIcon(bitmap);
    }

    private void SetupWindowHandlers()
    {
        if (_mainWindow == null) return;

        // Handle window closing to minimize to tray instead of closing
        _mainWindow.Closing += OnWindowClosing;
        
        // Handle window state changes to hide to tray when minimized
        _mainWindow.PropertyChanged += OnWindowPropertyChanged;
    }

    private void OnWindowPropertyChanged(object? sender, AvaloniaPropertyChangedEventArgs e)
    {
        // Only handle minimize-to-tray if tray is actually supported
        if (e.Property == Window.WindowStateProperty && 
            e.NewValue is WindowState newState && 
            newState == WindowState.Minimized &&
            _minimizeToTray && 
            _isTraySupported &&
            _trayIcon != null &&
            !_isDisposed)
        {
            // Hide to tray when minimized (Task Manager behavior)
            HideMainWindow();
        }
    }

    private void OnWindowClosing(object? sender, WindowClosingEventArgs e)
    {
        // Only minimize to tray if tray is actually supported
        if (_minimizeToTray && _isTraySupported && _trayIcon != null && !_isDisposed)
        {
            e.Cancel = true;
            HideMainWindow();
        }
    }

    /// <inheritdoc />
    public void HideMainWindow()
    {
        if (_mainWindow == null) return;
        
        // If tray is not supported, don't hide - just minimize normally
        if (!_isTraySupported || _trayIcon == null)
        {
            Log.Debug("Tray not supported, minimizing to taskbar instead");
            _mainWindow.WindowState = WindowState.Minimized;
            return;
        }

        // Hide from taskbar (may not work on all Linux DEs)
        try
        {
            _mainWindow.ShowInTaskbar = false;
        }
        catch (Exception ex)
        {
            Log.Debug(ex, "ShowInTaskbar not supported on this platform");
        }
        
        _mainWindow.Hide();
        _trayIcon.IsVisible = true;
        
        Log.Debug("Main window hidden to tray");
    }

    /// <inheritdoc />
    public void ShowMainWindow()
    {
        if (_mainWindow == null) return;

        // Restore taskbar visibility
        try
        {
            _mainWindow.ShowInTaskbar = true;
        }
        catch (Exception ex)
        {
            Log.Debug(ex, "ShowInTaskbar not supported on this platform");
        }
        
        _mainWindow.Show();
        _mainWindow.WindowState = WindowState.Normal;
        
        // Activate/focus the window (platform-specific behavior)
        try
        {
            _mainWindow.Activate();
        }
        catch (Exception ex)
        {
            Log.Debug(ex, "Window.Activate not fully supported on this platform");
        }
        
        // Keep icon visible if activity graph is enabled
        if (_trayIcon != null && !_minimizeToTray && !_showActivityGraph)
        {
            _trayIcon.IsVisible = false;
        }
        
        Log.Debug("Main window shown from tray");
    }

    private void ExitApplication()
    {
        _isDisposed = true; // Prevent re-hiding when we're actually exiting
        
        if (Application.Current?.ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
        {
            desktop.Shutdown();
        }
    }

    private void UpdateTrayIconVisibility()
    {
        if (_trayIcon == null) return;

        // Always visible if activity graph is enabled
        if (_showActivityGraph)
        {
            _trayIcon.IsVisible = true;
            return;
        }
        
        // If minimize to tray is disabled and window is visible, hide the tray icon
        if (!_minimizeToTray && _mainWindow?.IsVisible == true)
        {
            _trayIcon.IsVisible = false;
        }
    }

    /// <inheritdoc />
    public void Dispose()
    {
        if (_isDisposed) return;
        _isDisposed = true;

        if (_mainWindow != null)
        {
            _mainWindow.Closing -= OnWindowClosing;
            _mainWindow.PropertyChanged -= OnWindowPropertyChanged;
        }

        if (_trayIcon != null)
        {
            var trayIcons = TrayIcon.GetIcons(Application.Current!);
            trayIcons?.Remove(_trayIcon);
            _trayIcon.Dispose();
            _trayIcon = null;
        }

        Log.Information("TrayIconService disposed");
    }
}
