using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Media.Imaging;
using Avalonia.Threading;
using Serilog;
using SkiaSharp;
using WireBound.Core.Helpers;
using WireBound.Core.Models;
using WireBound.Core.Services;

namespace WireBound.Avalonia.Services;

/// <summary>
/// Service for managing the system tray icon functionality.
/// Cross-platform implementation supporting Windows and Linux (with AppIndicator).
/// Features a dynamic activity graph similar to Windows Task Manager.
/// </summary>
public sealed class TrayIconService : ITrayIconService
{
    private TrayIcon? _trayIcon;
    private Window? _mainWindow;
    private bool _isDisposed;
    private bool _minimizeToTray;
    private TrayIconMode _iconMode = TrayIconMode.Traffic;
    private string _trafficAdapterId = string.Empty;
    private bool _isTraySupported = true;
    private NativeMenuItem? _updateMenuItem;

    /// <summary>
    /// True when the icon shows a live metric graph (anything but the static app icon).
    /// Live modes keep the tray icon permanently visible.
    /// </summary>
    private bool IsLiveMode => _iconMode != TrayIconMode.AppIcon;

    // Activity graph data - stores last N readings for the mini-graphs
    private const int GraphHistorySize = 16;
    private readonly Queue<(float download, float upload)> _activityHistory = new();
    private readonly Queue<float> _cpuHistory = new();
    private readonly Queue<float> _ramHistory = new();
    private long _autoScaleMaxSpeed = 1_000_000; // 1 MB/s default, auto-adjusts

    // Last metric values (used for tooltip composition)
    private long _lastDownloadBps;
    private long _lastUploadBps;
    private double _lastCpuPercent;
    private double _lastRamPercent;

    // Memory pressure state (updated via UpdateMemoryPressure, consumed on UI thread)
    private MemoryPressureLevel _memoryPressureLevel = MemoryPressureLevel.Normal;
    private string _memoryTooltipLine = string.Empty;

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
    /// Gets or sets which network adapter's traffic the tray shows in Traffic mode.
    /// Plain data holder read by the polling service; setting it has no immediate UI effect.
    /// </summary>
    public string TrafficAdapterId
    {
        get => _trafficAdapterId;
        set => _trafficAdapterId = value ?? string.Empty;
    }

    /// <summary>
    /// Gets or sets what the tray icon displays. Changing it re-renders the icon
    /// for the new mode immediately (marshalling to the UI thread when required)
    /// and updates icon visibility.
    /// </summary>
    public TrayIconMode IconMode
    {
        get => _iconMode;
        set
        {
            _iconMode = value;
            if (_trayIcon == null || _isDisposed) return;

            if (!Dispatcher.UIThread.CheckAccess())
            {
                Dispatcher.UIThread.Post(ApplyIconModeChange);
                return;
            }

            ApplyIconModeChange();
        }
    }

    private void ApplyIconModeChange()
    {
        if (_trayIcon == null || _isDisposed) return;

        var icon = CreateIconForCurrentMode();
        if (icon != null) _trayIcon.Icon = icon;
        UpdateTrayIconVisibility();
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
        // Windows generally supports tray icons
        if (OperatingSystem.IsWindows())
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
    /// Picks the initial tray icon at setup time based on the active
    /// <see cref="IconMode"/>. Live graph modes render their chart immediately —
    /// with no samples yet they draw as an empty graph frame, so the user sees the
    /// chart UI from the first frame instead of a static PNG that flickers to a
    /// graph one second later. <see cref="TrayIconMode.AppIcon"/> (or a failed
    /// render) falls back to the brand PNG.
    /// </summary>
    private WindowIcon? CreateIconBitmap() => CreateIconForCurrentMode();

    /// <summary>
    /// Renders the icon appropriate for the current <see cref="IconMode"/>,
    /// falling back to the static brand icon when a graph can't be produced.
    /// </summary>
    private WindowIcon? CreateIconForCurrentMode() => _iconMode switch
    {
        TrayIconMode.Traffic => CreateActivityGraphIcon() ?? CreateStaticIcon(),
        TrayIconMode.Cpu => CreateMetricGraphIcon(_cpuHistory, ChartColors.CpuColor) ?? CreateStaticIcon(),
        TrayIconMode.Ram => CreateMetricGraphIcon(_ramHistory, ChartColors.MemoryColor) ?? CreateStaticIcon(),
        _ => CreateStaticIcon(),
    };

    /// <summary>
    /// Creates the static tray icon from the app's bundled
    /// <c>Assets/wirebound-16.png</c> (or 32px fallback) so the tray matches
    /// the rest of the app's brand iconography. Falls back to the procedural
    /// SkiaSharp lightning bolt if the asset can't be loaded — that path
    /// guarantees the tray is always usable, even in unbundled dev runs.
    /// </summary>
    private static WindowIcon? CreateStaticIcon()
    {
        try
        {
            // Try the bundled brand icon first. Avalonia's AssetLoader resolves
            // avares:// URIs against any AvaloniaResource-bundled file. The
            // 16px asset is sized exactly for the tray on Windows.
            var assetUri = new Uri("avares://WireBound/Assets/wirebound-16.png");
            if (global::Avalonia.Platform.AssetLoader.Exists(assetUri))
            {
                using var stream = global::Avalonia.Platform.AssetLoader.Open(assetUri);
                var bitmap = new global::Avalonia.Media.Imaging.Bitmap(stream);
                return new WindowIcon(bitmap);
            }
        }
        catch (Exception ex)
        {
            Log.Debug(ex, "Could not load tray icon from assets; falling back to procedural");
        }

        // Fallback: procedural cyan circle + lightning bolt. Kept for safety.
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
    /// Updates the tray icon with the latest network and system metrics.
    /// Appends all metric histories (so switching modes stays warm), renders at
    /// most one icon based on the active <see cref="IconMode"/>, and refreshes the tooltip.
    /// </summary>
    public void UpdateMetrics(long downloadSpeedBps, long uploadSpeedBps, double cpuPercent, double ramPercent)
    {
        if (_trayIcon == null || _isDisposed) return;

        // Dispatch to UI thread if not already on it
        if (!Dispatcher.UIThread.CheckAccess())
        {
            Dispatcher.UIThread.Post(() => UpdateMetrics(downloadSpeedBps, uploadSpeedBps, cpuPercent, ramPercent));
            return;
        }

        _lastDownloadBps = downloadSpeedBps;
        _lastUploadBps = uploadSpeedBps;
        _lastCpuPercent = cpuPercent;
        _lastRamPercent = ramPercent;

        // Auto-scale network: track the maximum speed seen
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

        var effectiveMax = (float)_autoScaleMaxSpeed;
        Enqueue(_activityHistory, (
            Math.Min(1f, downloadSpeedBps / effectiveMax),
            Math.Min(1f, uploadSpeedBps / effectiveMax)));

        // CPU/RAM are percentages (0-100); normalize to 0-1 for the graph.
        Enqueue(_cpuHistory, (float)Math.Clamp(cpuPercent / 100.0, 0, 1));
        Enqueue(_ramHistory, (float)Math.Clamp(ramPercent / 100.0, 0, 1));

        // Render at most one icon for the active mode (AppIcon renders nothing here).
        var icon = _iconMode switch
        {
            TrayIconMode.Traffic => CreateActivityGraphIcon(),
            TrayIconMode.Cpu => CreateMetricGraphIcon(_cpuHistory, ChartColors.CpuColor),
            TrayIconMode.Ram => CreateMetricGraphIcon(_ramHistory, ChartColors.MemoryColor),
            _ => null,
        };
        if (icon != null) _trayIcon.Icon = icon;

        RefreshTooltip();
    }

    private static void Enqueue<T>(Queue<T> history, T value)
    {
        history.Enqueue(value);
        while (history.Count > GraphHistorySize)
        {
            history.Dequeue();
        }
    }

    /// <inheritdoc />
    public void SetUpdateAvailable(string? version, Action? onClicked)
    {
        if (_isDisposed) return;

        Dispatcher.UIThread.Post(() =>
        {
            if (_isDisposed) return;

            if (_updateMenuItem != null)
            {
                _trayIcon?.Menu?.Items.Remove(_updateMenuItem);
                _updateMenuItem = null;
            }

            if (version != null && onClicked != null && _trayIcon?.Menu != null)
            {
                _updateMenuItem = new NativeMenuItem($"Update available: v{version}");
                _updateMenuItem.Click += (_, _) => onClicked();
                // Insert after "Show WireBound" (position 1), before separator
                var insertIndex = Math.Min(1, _trayIcon.Menu.Items.Count);
                _trayIcon.Menu.Items.Insert(insertIndex, _updateMenuItem);
            }
        });
    }

    /// <summary>
    /// Draws the shared graph chrome — memory-pressure-tinted background, border,
    /// and subtle horizontal grid lines — used by every live graph mode.
    /// </summary>
    private void DrawGraphFrame(SKCanvas canvas)
    {
        // Dark background tinted by memory pressure level
        canvas.Clear(_memoryPressureLevel switch
        {
            MemoryPressureLevel.Warning => new SKColor(60, 50, 20),  // Amber tint
            MemoryPressureLevel.Critical => new SKColor(60, 20, 20), // Coral tint
            _ => new SKColor(20, 30, 35)                             // Default dark blue-gray
        });

        using var borderPaint = new SKPaint
        {
            Color = new SKColor(60, 80, 90),
            IsAntialias = false,
            Style = SKPaintStyle.Stroke,
            StrokeWidth = 1
        };
        canvas.DrawRect(0, 0, IconSize - 1, IconSize - 1, borderPaint);

        using var gridPaint = new SKPaint
        {
            Color = new SKColor(40, 55, 60),
            IsAntialias = false,
            Style = SKPaintStyle.Stroke,
            StrokeWidth = 1
        };
        for (int y = 4; y < IconSize - 1; y += 4)
        {
            canvas.DrawLine(1, y, IconSize - 2, y, gridPaint);
        }
    }

    /// <summary>
    /// Creates a Task Manager-style activity graph icon showing network activity.
    /// The graph shows download (cyan) and upload (pink) as stacked area bars.
    /// </summary>
    private WindowIcon? CreateActivityGraphIcon()
    {
        try
        {
            using var surface = SKSurface.Create(new SKImageInfo(IconSize, IconSize, SKColorType.Rgba8888, SKAlphaType.Premul));
            var canvas = surface.Canvas;

            DrawGraphFrame(canvas);

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
                DrawEmptyBaseline(canvas, new SKColor(0, 229, 255, 100));
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
    /// Creates a single-series graph icon (used for CPU and RAM modes), drawing the
    /// normalized history as bars from the bottom up in the supplied series color.
    /// </summary>
    private WindowIcon? CreateMetricGraphIcon(Queue<float> history, SKColor seriesColor)
    {
        try
        {
            using var surface = SKSurface.Create(new SKImageInfo(IconSize, IconSize, SKColorType.Rgba8888, SKAlphaType.Premul));
            var canvas = surface.Canvas;

            DrawGraphFrame(canvas);

            using var seriesPaint = new SKPaint
            {
                Color = seriesColor,
                IsAntialias = false,
                Style = SKPaintStyle.Fill
            };

            var data = history.ToArray();
            var graphWidth = IconSize - 2;
            var graphHeight = IconSize - 2;
            var barWidth = (float)graphWidth / GraphHistorySize;

            for (int i = 0; i < data.Length; i++)
            {
                var value = data[i];
                var barHeight = value * (graphHeight - 1);
                if (barHeight > 0.5f)
                {
                    var x = 1 + i * barWidth;
                    canvas.DrawRect(
                        x, IconSize - 1 - barHeight,
                        Math.Max(1, barWidth - 0.5f), barHeight,
                        seriesPaint);
                }
            }

            if (data.Length == 0)
            {
                DrawEmptyBaseline(canvas, seriesColor.WithAlpha(100));
            }

            return CreateWindowIconFromSurface(surface);
        }
        catch (Exception ex)
        {
            Log.Warning(ex, "Failed to create metric graph icon");
            return null;
        }
    }

    private static void DrawEmptyBaseline(SKCanvas canvas, SKColor color)
    {
        using var emptyPaint = new SKPaint
        {
            Color = color,
            IsAntialias = false,
            Style = SKPaintStyle.Fill
        };
        canvas.DrawRect(1, IconSize - 2, IconSize - 2, 1, emptyPaint);
    }

    /// <summary>
    /// Refreshes the tray tooltip from the most recent metric values, including a
    /// CPU/RAM line for those modes and the memory-pressure warning line when present.
    /// </summary>
    private void RefreshTooltip()
    {
        if (_trayIcon == null) return;

        var downloadSpeed = ByteFormatter.FormatSpeed(_lastDownloadBps);
        var uploadSpeed = ByteFormatter.FormatSpeed(_lastUploadBps);

        var tooltip = $"WireBound\n↓ {downloadSpeed}  ↑ {uploadSpeed}";

        tooltip += _iconMode switch
        {
            TrayIconMode.Cpu => $"\nCPU: {_lastCpuPercent:F0}%",
            TrayIconMode.Ram => $"\nRAM: {_lastRamPercent:F0}%",
            _ => string.Empty,
        };

        if (!string.IsNullOrEmpty(_memoryTooltipLine))
        {
            tooltip += $"\n{_memoryTooltipLine}";
        }

        _trayIcon.ToolTipText = tooltip;
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
        // Allow OS shutdown and application shutdown to proceed without interference —
        // cancelling these causes Windows to show "app is preventing shutdown" dialogs.
        if (e.CloseReason is WindowCloseReason.OSShutdown or WindowCloseReason.ApplicationShutdown)
            return;

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

        // Re-evaluate tray icon visibility now that the window is shown again.
        UpdateTrayIconVisibility();

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

        // Live metric modes (traffic/CPU/RAM) keep the icon permanently visible.
        if (IsLiveMode)
        {
            _trayIcon.IsVisible = true;
            return;
        }

        // Static app-icon mode: the icon's only job is to provide access while
        // the window is hidden, so keep it visible when the window is hidden,
        // and otherwise show it only when it serves the minimize-to-tray purpose.
        _trayIcon.IsVisible = _mainWindow?.IsVisible != true || _minimizeToTray;
    }

    /// <inheritdoc />
    public void UpdateMemoryPressure(MemoryPressureLevel level, double usagePercent, long availableBytes, long swapUsedBytes)
    {
        if (_isDisposed) return;

        if (!Dispatcher.UIThread.CheckAccess())
        {
            Dispatcher.UIThread.Post(() => UpdateMemoryPressure(level, usagePercent, availableBytes, swapUsedBytes));
            return;
        }

        _memoryPressureLevel = level;
        _memoryTooltipLine = level > MemoryPressureLevel.Normal
            ? $"⚠ RAM: {usagePercent:F0}% ({ByteFormatter.FormatBytes(availableBytes)} free{(swapUsedBytes > 0 ? $", swap {ByteFormatter.FormatBytes(swapUsedBytes)}" : "")})"
            : string.Empty;
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
            if (Application.Current is { } app)
            {
                var trayIcons = TrayIcon.GetIcons(app);
                trayIcons?.Remove(_trayIcon);
            }

            _trayIcon.Dispose();
            _trayIcon = null;
        }

        Log.Information("TrayIconService disposed");
    }
}
