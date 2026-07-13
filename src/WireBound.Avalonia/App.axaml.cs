using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Markup.Xaml;
using Avalonia.Threading;
using CommunityToolkit.Mvvm.Messaging;
using LiveChartsCore;
using LiveChartsCore.SkiaSharpView;
using Microsoft.Extensions.DependencyInjection;
using Serilog;
using WireBound.Avalonia.Services;
using WireBound.Avalonia.ViewModels;
using WireBound.Avalonia.Views;
using WireBound.Core.Data;
using WireBound.Core.Services;
using WireBound.Platform.Abstract.Services;
using WireBound.Platform.Linux;
using WireBound.Platform.Stub;
using WireBound.Platform.Windows;

namespace WireBound.Avalonia;

public partial class App : Application
{
    private ServiceProvider? _serviceProvider;
    private volatile bool _shutdownInProgress;

    public override void Initialize()
    {
        AvaloniaXamlLoader.Load(this);

        // Configure LiveCharts
        LiveCharts.Configure(config => config
            .AddSkiaSharp()
            .AddDefaultMappers());
    }

    public override void OnFrameworkInitializationCompleted()
    {
        // Build dependency injection container
        var services = new ServiceCollection();
        ConfigureServices(services);
        _serviceProvider = services.BuildServiceProvider();

        // Initialize static localization accessor
        var localizationService = _serviceProvider.GetRequiredService<ILocalizationService>();
        Strings.Initialize(localizationService);

        // Initialize database (synchronous, fast operation)
        InitializeDatabase();

        if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
        {
            // Create main window with navigation
            var mainViewModel = _serviceProvider.GetRequiredService<MainViewModel>();
            var mainWindow = new MainWindow
            {
                DataContext = mainViewModel
            };
            desktop.MainWindow = mainWindow;

            desktop.ShutdownRequested += OnShutdownRequested;

            // Fire and forget async initialization - avoids blocking UI thread
            _ = InitializeAsyncServicesAsync(mainWindow);

            Log.Information("WireBound Avalonia application started successfully");
        }

        base.OnFrameworkInitializationCompleted();
    }

    /// <summary>
    /// Handles all async initialization to avoid blocking the UI thread.
    /// Uses fire-and-forget pattern with proper error handling.
    /// </summary>
    private async Task InitializeAsyncServicesAsync(MainWindow mainWindow)
    {
        try
        {
            // Ensure startup entry points to current executable (handles updates that change install path)
            await EnsureStartupPathUpdatedAsync();

            // Try to silently connect to / start an existing auto-started helper.
            // This MUST run before the first per-process query but does not block
            // the UI — failures are logged and surfaced via Settings.
            await TryAutoConnectHelperAsync();

            // Initialize tray icon and apply settings
            await InitializeTrayIconAsync(mainWindow);

            // Apply saved theme preference
            await ApplyThemeFromSettingsAsync();

            // Check if we should start minimized
            await ApplyStartMinimizedSettingAsync(mainWindow);

            // Start background services
            await StartBackgroundServicesAsync();

            // Check for updates (non-blocking, privacy-safe)
            await CheckForUpdatesAsync();

            // Show What's New dialog if this is a post-update restart
            await ShowWhatsNewIfUpdatedAsync(mainWindow);
        }
        catch (Exception ex)
        {
            Log.Error(ex, "Failed to complete async initialization");
        }
    }

    /// <summary>
    /// Silently attempts to connect to a pre-running elevated helper, or to
    /// start one via the registered Task Scheduler / systemd hook (no UAC /
    /// pkexec prompt).
    ///
    /// <para>
    /// Sequence:
    /// </para>
    /// <list type="number">
    ///   <item>If the user has not opted into auto-start (<c>IsHelperStartupEnabledAsync</c>
    ///         returns false), do nothing. The user must explicitly enable the
    ///         toggle in Settings (which triggers a one-time UAC/pkexec to
    ///         register the system hook).</item>
    ///   <item>Validate the registered hook still points at the expected helper
    ///         binary via <c>ValidateRegistrationAsync</c>. If the registration
    ///         has been mutated, refuse to auto-connect and surface a warning
    ///         in Settings — never trust a tampered scheduled task / unit.</item>
    ///   <item>Try a short-timeout connect to an existing helper instance
    ///         (helper likely already running from login).</item>
    ///   <item>If no existing helper, attempt to start one via the silent path
    ///         (<c>allowInteractive: false</c> — guarantees no UAC prompt).</item>
    ///   <item>On any failure, log warning, fire connection-state event, never
    ///         block. App keeps working in cross-platform mode.</item>
    /// </list>
    ///
    /// <para>
    /// Total budget is ~6 seconds. The state surfaces via
    /// <see cref="IElevationService.HelperConnectionStateChanged"/> so Settings
    /// can show "Registered / Running / Connected / Tampered" without blocking
    /// startup.
    /// </para>
    /// </summary>
    private async Task TryAutoConnectHelperAsync()
    {
        if (_serviceProvider is null) return;

        try
        {
            var startupService = _serviceProvider.GetRequiredService<IStartupService>();
            var elevationService = _serviceProvider.GetRequiredService<IElevationService>();

            if (!elevationService.IsElevationSupported)
            {
                Log.Debug("Elevation not supported on this platform; skipping auto-connect");
                return;
            }

            if (!startupService.IsHelperStartupSupported)
            {
                Log.Debug("Helper auto-start not supported on this platform");
                return;
            }

            var enabled = await startupService.IsHelperStartupEnabledAsync();
            if (!enabled)
            {
                Log.Debug("Helper auto-start not enabled by user; skipping");
                return;
            }

            // Task / unit definition integrity check — refuse to invoke a
            // registration that has been mutated.
            var helperManager = _serviceProvider.GetService<IHelperProcessManager>();
            if (helperManager is not null)
            {
                var regValid = await helperManager.ValidateRegistrationAsync();
                if (!regValid.IsValid)
                {
                    Log.Warning(
                        "Helper auto-start registration is invalid: {Reason}. " +
                        "Refusing to auto-connect — user must repair via Settings.",
                        regValid.ErrorMessage);
                    return;
                }
            }

            // Step 1: try to connect to an existing helper (likely already
            // started by Task Scheduler / systemd at login).
            var existing = await elevationService.TryConnectExistingAsync(timeoutMs: 2500);
            if (existing.IsSuccess)
            {
                Log.Information("Auto-connected to existing elevation helper");
                return;
            }

            // Step 2: not running — try the silent start path (no UAC fallback).
            if (elevationService is WireBound.Platform.Windows.Services.WindowsElevationService winSvc)
            {
                // The Windows service exposes the allowInteractive flag via its
                // internal StartHelperInternalAsync; we call StartHelperAsync
                // which delegates with allowInteractive=true, but the helper
                // manager itself respects the flag we pass. For the auto-start
                // path we want allowInteractive=false everywhere.
                // We accomplish that by invoking the helper manager directly.
                if (helperManager is not null)
                {
                    var startResult = await helperManager.StartAsync(allowInteractive: false);
                    if (!startResult.IsSuccess)
                    {
                        Log.Information(
                            "Helper auto-start could not start helper silently: {Status} ({Error}). " +
                            "User can manually enable via Settings.",
                            startResult.Status, startResult.ErrorMessage);
                        return;
                    }
                    // Helper started — now connect.
                    var conn = await elevationService.TryConnectExistingAsync(timeoutMs: 3000);
                    if (!conn.IsSuccess)
                    {
                        Log.Warning(
                            "Helper started but auto-connect failed: {Status} ({Error})",
                            conn.Status, conn.ErrorMessage);
                    }
                    else
                    {
                        Log.Information("Auto-started and connected to elevation helper");
                    }
                }
                return;
            }

            // Linux: same approach via the manager.
            if (helperManager is not null)
            {
                var startResult = await helperManager.StartAsync(allowInteractive: false);
                if (!startResult.IsSuccess)
                {
                    Log.Information(
                        "Helper auto-start could not start helper silently: {Status} ({Error}). " +
                        "User can manually enable via Settings.",
                        startResult.Status, startResult.ErrorMessage);
                    return;
                }
                var conn = await elevationService.TryConnectExistingAsync(timeoutMs: 3000);
                if (!conn.IsSuccess)
                {
                    Log.Warning("Helper started but auto-connect failed: {Error}", conn.ErrorMessage);
                }
                else
                {
                    Log.Information("Auto-started and connected to elevation helper");
                }
            }
        }
        catch (Exception ex)
        {
            // Never let auto-connect bubble up — degraded mode is preferable to crash
            Log.Error(ex, "Helper auto-connect threw unexpectedly");
        }
    }

    private void ConfigureServices(IServiceCollection services)
    {
        // Register logging, forwarding Microsoft.Extensions.Logging to Serilog
        services.AddLogging(builder => builder.AddSerilog());

        // Register Database
        services.AddDbContext<WireBoundDbContext>();

        // Register cross-platform network monitoring service
        // Uses System.Net.NetworkInformation which works on Windows and Linux
        services.AddSingleton<INetworkMonitorService, CrossPlatformNetworkMonitorService>();

        // Register data persistence with segregated interfaces for ISP compliance
        // The DataPersistenceService implements all focused repository interfaces
        services.AddSingleton<DataPersistenceService>();
        services.AddSingleton<IDataPersistenceService>(sp => sp.GetRequiredService<DataPersistenceService>());
        services.AddSingleton<INetworkUsageRepository>(sp => sp.GetRequiredService<DataPersistenceService>());
        services.AddSingleton<IAppUsageRepository>(sp => sp.GetRequiredService<DataPersistenceService>());
        services.AddSingleton<ISettingsRepository>(sp => sp.GetRequiredService<DataPersistenceService>());
        services.AddSingleton<ISpeedSnapshotRepository>(sp => sp.GetRequiredService<DataPersistenceService>());
        services.AddSingleton<ISystemSnapshotRepository>(sp => sp.GetRequiredService<DataPersistenceService>());

        services.AddSingleton<IWiFiInfoService, WiFiInfoService>();

        // Register platform services (stub first, then override with platform-specific)
        StubPlatformServices.Instance.Register(services);

        if (OperatingSystem.IsWindows())
        {
            WindowsPlatformServices.Instance.Register(services);
        }
        else if (OperatingSystem.IsLinux())
        {
            LinuxPlatformServices.Instance.Register(services);
        }

        // Register per-app network tracking service (adapts platform providers)
        services.AddSingleton<IProcessNetworkService, ProcessNetworkService>();

        // Register system monitoring service (CPU, RAM)
        services.AddSingleton<ISystemMonitorService, SystemMonitorService>();

        // Register system history service for historical stats tracking
        services.AddSingleton<ISystemHistoryService, SystemHistoryService>();

        // Register resource insights services (per-app CPU + memory grouping & smoothing)
        services.AddSingleton<IAppCategoryService, AppCategoryService>();
        services.AddSingleton<IResourceInsightsService, ResourceInsightsService>();
        services.AddSingleton<IAppOverviewService, AppOverviewService>();
        services.AddSingleton<AppGroupingService>();

        // Register DNS resolver service for reverse lookups
        services.AddSingleton<IDnsResolverService, DnsResolverService>();

        // Register data export service
        services.AddSingleton<IDataExportService, DataExportService>();

        // Register update check service (Velopack for installed mode, GitHub API fallback for portable)
        services.AddSingleton<IUpdateService, VelopackUpdateService>();

        // Register UI thread dispatcher abstraction
        services.AddSingleton<IUiDispatcher, AvaloniaDispatcher>();
        services.AddSingleton<IClipboardService, AvaloniaClipboardService>();

        // Register system time provider (injectable for testability)
        services.AddSingleton(TimeProvider.System);

        // Register app-specific services
        services.AddSingleton<INavigationService, NavigationService>();
        services.AddSingleton<ILocalizationService, LocalizationService>();
        services.AddSingleton<IViewFactory, ViewFactory>();
        services.AddSingleton<ITrayIconService, TrayIconService>();

        // Register background service
        services.AddSingleton<NetworkPollingBackgroundService>();
        services.AddSingleton<INetworkPollingBackgroundService>(sp =>
            sp.GetRequiredService<NetworkPollingBackgroundService>());

        // Register ViewModels
        services.AddSingleton<MainViewModel>();
        services.AddSingleton<OverviewViewModel>();
        services.AddSingleton<ChartsViewModel>();
        services.AddSingleton<SettingsViewModel>();
        services.AddSingleton<AppsViewModel>();
        services.AddSingleton<ConnectionsViewModel>();
        services.AddSingleton<SystemViewModel>();
        services.AddSingleton<HistoryViewModel>();

        // Register View factory for navigation
        services.AddTransient<OverviewView>();
        services.AddTransient<ChartsView>();
        services.AddTransient<SettingsView>();
        services.AddTransient<AppsView>();
        services.AddTransient<ConnectionsView>();
        services.AddTransient<SystemView>();
        services.AddTransient<HistoryView>();
    }

    [System.Diagnostics.CodeAnalysis.SuppressMessage("AOT", "IL3050", Justification = "EF Core AOT: EnsureCreated uses runtime model building; ApplyMigrations uses raw SQL which is AOT-safe")]
    private void InitializeDatabase()
    {
        try
        {
            using var scope = _serviceProvider!.CreateScope();
            var context = scope.ServiceProvider.GetRequiredService<WireBoundDbContext>();
            context.Database.EnsureCreated();
            context.ApplyMigrations();
            Log.Information("Database initialized successfully");
        }
        catch (Exception ex)
        {
            Log.Error(ex, "Failed to initialize database");
        }
    }

    private async Task EnsureStartupPathUpdatedAsync()
    {
        try
        {
            var startupService = _serviceProvider!.GetRequiredService<IStartupService>();
            if (!startupService.IsStartupSupported)
            {
                return;
            }

            var result = await startupService.EnsureStartupPathUpdatedAsync();
            if (!result)
            {
                Log.Warning("Failed to ensure startup path is updated");
            }

            // Also update the helper startup path if registered
            if (startupService.IsHelperStartupSupported)
            {
                var helperResult = await startupService.EnsureHelperStartupPathUpdatedAsync();
                if (!helperResult)
                {
                    Log.Warning("Failed to ensure helper startup path is updated");
                }
            }
        }
        catch (Exception ex)
        {
            Log.Error(ex, "Failed to ensure startup path is updated");
        }
    }

    private async Task StartBackgroundServicesAsync()
    {
        try
        {
            var pollingService = _serviceProvider!.GetRequiredService<NetworkPollingBackgroundService>();
            await pollingService.StartAsync(CancellationToken.None);
            Log.Information("Background polling service started");

            // Eagerly construct AppsViewModel so its live-CPU sampling timer
            // starts running at app startup, not on first navigation to Apps.
            // Without this, users who land on (and stay on) Overview never get
            // a populated Apps tab when they finally visit it — and worse,
            // they see "—" placeholders for ~10s after navigation because the
            // 60s rolling window starts empty. Singleton lifetime means this
            // is a one-shot cost.
            _ = _serviceProvider!.GetRequiredService<AppsViewModel>();
        }
        catch (Exception ex)
        {
            Log.Error(ex, "Failed to start background services");
        }
    }

    private async Task InitializeTrayIconAsync(MainWindow mainWindow)
    {
        if (_serviceProvider is null) return;

        try
        {
            // Load settings to get minimize to tray preference
            var settingsRepository = _serviceProvider.GetRequiredService<ISettingsRepository>();
            var settings = await settingsRepository.GetSettingsAsync();

            var trayIconService = (TrayIconService)_serviceProvider.GetRequiredService<ITrayIconService>();

            // Tray icon initialization may touch UI, ensure we're on UI thread
            await Dispatcher.UIThread.InvokeAsync(() =>
            {
                trayIconService.Initialize(mainWindow, settings.MinimizeToTray);
                trayIconService.IconMode = settings.TrayIconMode;
                trayIconService.TrafficAdapterId = settings.TrayTrafficAdapterId;
            });

            // Subscribe to settings changes
            var settingsViewModel = _serviceProvider.GetRequiredService<SettingsViewModel>();
            settingsViewModel.PropertyChanged += (_, e) =>
            {
                switch (e.PropertyName)
                {
                    case nameof(SettingsViewModel.MinimizeToTray):
                        trayIconService.MinimizeToTray = settingsViewModel.MinimizeToTray;
                        break;
                    case nameof(SettingsViewModel.SelectedTrayIconMode):
                        trayIconService.IconMode = settingsViewModel.SelectedTrayIconMode;
                        break;
                    case nameof(SettingsViewModel.SelectedTrayAdapter):
                        trayIconService.TrafficAdapterId = settingsViewModel.SelectedTrayAdapter?.Id ?? string.Empty;
                        break;
                }
            };

            Log.Information("Tray icon initialized with MinimizeToTray={MinimizeToTray}, IconMode={IconMode}",
                settings.MinimizeToTray, settings.TrayIconMode);
        }
        catch (Exception ex)
        {
            Log.Error(ex, "Failed to initialize tray icon");
        }
    }

    private async Task ApplyThemeFromSettingsAsync()
    {
        if (_serviceProvider is null) return;

        try
        {
            var settingsRepository = _serviceProvider.GetRequiredService<ISettingsRepository>();
            var settings = await settingsRepository.GetSettingsAsync();
            await Dispatcher.UIThread.InvokeAsync(() =>
                Helpers.ThemeHelper.ApplyTheme(settings.Theme));
            Log.Information("Applied theme: {Theme}", settings.Theme);
        }
        catch (Exception ex)
        {
            Log.Error(ex, "Failed to apply theme from settings");
        }
    }

    private async Task ApplyStartMinimizedSettingAsync(MainWindow mainWindow)
    {
        if (_serviceProvider is null) return;

        try
        {
            var settingsRepository = _serviceProvider.GetRequiredService<ISettingsRepository>();
            var settings = await settingsRepository.GetSettingsAsync();

            if (settings.StartMinimized)
            {
                // Use the tray service to hide properly (handles cross-platform differences)
                // UI operations must be on UI thread
                await Dispatcher.UIThread.InvokeAsync(() =>
                {
                    var trayService = _serviceProvider.GetRequiredService<ITrayIconService>();
                    trayService.HideMainWindow();
                });

                Log.Information("Application started minimized to tray");
            }
        }
        catch (Exception ex)
        {
            Log.Error(ex, "Failed to apply start minimized setting");
        }
    }

    private async Task CheckForUpdatesAsync()
    {
        try
        {
            var settingsRepository = _serviceProvider!.GetRequiredService<ISettingsRepository>();
            var settings = await settingsRepository.GetSettingsAsync();
            if (!settings.CheckForUpdates) return;

            var updateService = _serviceProvider!.GetRequiredService<IUpdateService>();
            var update = await updateService.CheckForUpdateAsync();
            if (update is null) return;

            // Populate SettingsViewModel with update info
            var settingsVm = _serviceProvider!.GetRequiredService<SettingsViewModel>();
            settingsVm.UpdateAvailable = true;
            settingsVm.LatestVersion = update.Version;
            settingsVm.UpdateUrl = update.ReleaseNotesUrl;
            settingsVm.PendingUpdate = update;
            settingsVm.IsUpdateSupported = updateService.IsUpdateSupported;
            Log.Information("Update available: {Version}", update.Version);

            // Send nav badge message
            WeakReferenceMessenger.Default.Send(new UpdateAvailableMessage(update.Version));

            // Show tray icon update item
            var trayService = _serviceProvider!.GetService<ITrayIconService>();
            trayService?.SetUpdateAvailable(update.Version, () =>
            {
                Dispatcher.UIThread.Post(() =>
                {
                    trayService.ShowMainWindow();
                    _serviceProvider?.GetService<INavigationService>()?.NavigateTo(Core.Routes.Settings);
                });
            });

            // Auto-download if enabled, supported, and not on metered network
            if (settings.AutoDownloadUpdates && updateService.IsUpdateSupported)
            {
                try
                {
                    var costProvider = _serviceProvider!.GetRequiredService<INetworkCostProvider>();
                    if (!await costProvider.IsMeteredAsync())
                    {
                        await updateService.DownloadUpdateAsync(update);
                        settingsVm.IsReadyToRestart = true;
                        Log.Information("Update auto-downloaded, ready to restart");
                    }
                    else
                    {
                        Log.Information("Skipped auto-download: metered network detected");
                    }
                }
                catch (Exception ex)
                {
                    Log.Warning(ex, "Auto-download failed, user can download manually");
                }
            }
        }
        catch (Exception ex)
        {
            Log.Warning(ex, "Update check failed");
        }
    }

    private async Task ShowWhatsNewIfUpdatedAsync(MainWindow mainWindow)
    {
        try
        {
            var updatedTo = Environment.GetEnvironmentVariable("WIREBOUND_UPDATED_TO");
            if (string.IsNullOrEmpty(updatedTo)) return;

            // Clear the flag so it only shows once
            Environment.SetEnvironmentVariable("WIREBOUND_UPDATED_TO", null);

            // Fetch release notes from GitHub API
            var notes = await FetchReleaseNotesAsync(updatedTo);

            await Dispatcher.UIThread.InvokeAsync(() =>
            {
                var whatsNewWindow = new WhatsNewWindow(updatedTo, notes);
                whatsNewWindow.ShowDialog(mainWindow);
            });

            Log.Information("Showed What's New dialog for version {Version}", updatedTo);
        }
        catch (Exception ex)
        {
            Log.Warning(ex, "Failed to show What's New dialog");
        }
    }

    private static async Task<string> FetchReleaseNotesAsync(string version)
    {
        try
        {
            using var http = new HttpClient();
            http.DefaultRequestHeaders.UserAgent.ParseAdd("WireBound");
            http.Timeout = TimeSpan.FromSeconds(10);
            var tag = version.StartsWith('v') ? version : $"v{version}";
            var url = $"https://api.github.com/repos/adsamcik/WireBound/releases/tags/{tag}";
            var response = await http.GetStringAsync(url);

            using var doc = System.Text.Json.JsonDocument.Parse(response);
            if (doc.RootElement.TryGetProperty("body", out var bodyElement))
            {
                var body = bodyElement.GetString();
                if (!string.IsNullOrWhiteSpace(body))
                    return body;
            }

            return $"Updated to version {version}.";
        }
        catch
        {
            return $"Successfully updated to version {version}.";
        }
    }

    private async void OnShutdownRequested(object? sender, ShutdownRequestedEventArgs e)
    {
        // Avalonia raises ShutdownRequested on the UI thread with a sync handler
        // signature, but our cleanup is genuinely async (some services — e.g.
        // WindowsElevationService — implement only IAsyncDisposable).
        //
        // Use the cancel-then-shutdown pattern:
        //   1. First invocation: cancel the shutdown, run async cleanup, then
        //      re-trigger shutdown via desktop.Shutdown().
        //   2. Second invocation (raised by that desktop.Shutdown() call): the
        //      _shutdownInProgress guard lets it pass through so Avalonia tears
        //      down the application loop.
        if (_shutdownInProgress)
        {
            return;
        }

        e.Cancel = true;
        _shutdownInProgress = true;

        Log.Information("Application shutting down...");

        try
        {
            await ShutdownAsync().ConfigureAwait(true);
        }
        catch (Exception ex)
        {
            Log.Error(ex, "Unexpected error during application shutdown");
        }
        finally
        {
            if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
            {
                desktop.Shutdown();
            }
        }
    }

    /// <summary>
    /// Orchestrates graceful application shutdown.
    /// </summary>
    /// <remarks>
    /// <para>Stage 1 — Stop background services so in-flight work (e.g. the polling
    /// loop writing speed snapshots) completes before the persistence layer is
    /// disposed.</para>
    /// <para>Stage 2 — Dispose the DI container asynchronously. The container
    /// disposes every singleton it created (ViewModels, TrayIconService,
    /// ElevationService, ...) in reverse-registration order, calling
    /// <see cref="IAsyncDisposable.DisposeAsync"/> when implemented.
    /// Async disposal is mandatory: services like WindowsElevationService only
    /// implement IAsyncDisposable, and <c>ServiceProvider.Dispose()</c> throws
    /// <see cref="InvalidOperationException"/> when it encounters one.</para>
    /// </remarks>
    private async Task ShutdownAsync()
    {
        var provider = Interlocked.Exchange(ref _serviceProvider, null);
        if (provider is null)
        {
            return;
        }

        await StopBackgroundServicesAsync(provider).ConfigureAwait(false);
        await DisposeServiceProviderAsync(provider).ConfigureAwait(false);

        Log.Information("Application shutdown complete");
    }

    private static async Task StopBackgroundServicesAsync(IServiceProvider provider)
    {
        var pollingService = provider.GetService<NetworkPollingBackgroundService>();
        if (pollingService is null)
        {
            return;
        }

        try
        {
            using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(5));
            await pollingService.StopAsync(cts.Token).ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            Log.Warning("Background service stop timed out during shutdown");
        }
        catch (Exception ex)
        {
            Log.Error(ex, "Error stopping background service during shutdown");
        }
    }

    private static async Task DisposeServiceProviderAsync(ServiceProvider provider)
    {
        try
        {
            await provider.DisposeAsync().ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            Log.Error(ex, "Error disposing service provider during shutdown");
        }
    }
}
