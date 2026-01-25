using System.Runtime.Versioning;
using Microsoft.Extensions.Logging;
using WireBound.Platform.Abstract.Services;

namespace WireBound.Platform.Linux.Services;

/// <summary>
/// Linux implementation of <see cref="IElevationService"/>.
/// </summary>
/// <remarks>
/// <para>
/// This implementation uses a minimal elevated helper process pattern.
/// The main UI application NEVER runs as root. Instead, a separate
/// helper process (wirebound-helper) handles all elevated operations.
/// </para>
/// <para>
/// <b>Security Architecture:</b>
/// <list type="bullet">
/// <item>Main UI runs as standard user at all times</item>
/// <item>Helper is a minimal process that ONLY provides eBPF data collection</item>
/// <item>Helper validates connecting client before accepting commands</item>
/// <item>Communication via Unix domain sockets with authentication</item>
/// <item>Sessions are time-limited (8 hour max)</item>
/// <item>All operations are logged for security auditing</item>
/// </list>
/// </para>
/// <para>
/// <b>Helper Launch Options:</b>
/// <list type="bullet">
/// <item><b>pkexec:</b> PolicyKit-based elevation with graphical auth dialog</item>
/// <item><b>systemd service:</b> Pre-installed elevated service (preferred for production)</item>
/// </list>
/// </para>
/// <para>
/// <b>Helper Process Constraints:</b>
/// <list type="bullet">
/// <item>No file system write access (except logging)</item>
/// <item>No network access (except local IPC)</item>
/// <item>No ability to start other processes</item>
/// <item>Read-only access to eBPF network events</item>
/// </list>
/// </para>
/// </remarks>
[SupportedOSPlatform("linux")]
public sealed class LinuxElevationService : IElevationService, IAsyncDisposable
{
    private readonly ILogger<LinuxElevationService>? _logger;
    private readonly IHelperProcessManager? _helperManager;
    private readonly Lazy<bool> _isElevated;
    private IHelperConnection? _helperConnection;
    private readonly SemaphoreSlim _connectionLock = new(1, 1);
    private bool _disposed;

    /// <summary>
    /// Helper executable name on Linux.
    /// </summary>
    private const string HelperExecutableName = "wirebound-helper";

    public LinuxElevationService(
        ILogger<LinuxElevationService>? logger = null,
        IHelperProcessManager? helperManager = null)
    {
        _logger = logger;
        _helperManager = helperManager;
        _isElevated = new Lazy<bool>(CheckIsElevated);
    }

    /// <inheritdoc />
    public bool IsHelperConnected => _helperConnection?.IsConnected == true;

    /// <inheritdoc />
    public bool IsElevated => _isElevated.Value;

    /// <inheritdoc />
    public bool RequiresElevation => !IsHelperConnected;

    /// <inheritdoc />
    /// <remarks>
    /// Currently returns false until the Linux helper is implemented.
    /// </remarks>
    public bool IsElevationSupported => false; // TODO: Set to true when helper is implemented

    /// <inheritdoc />
    public event EventHandler<HelperConnectionStateChangedEventArgs>? HelperConnectionStateChanged;

    /// <inheritdoc />
    public async Task<ElevationResult> StartHelperAsync(CancellationToken cancellationToken = default)
    {
        if (_disposed)
        {
            return ElevationResult.Failed("Service has been disposed");
        }

        await _connectionLock.WaitAsync(cancellationToken);
        try
        {
            if (IsHelperConnected)
            {
                _logger?.LogDebug("Helper already connected, skipping start");
                return ElevationResult.Success();
            }

            // Log if running as root - this is a security warning
            if (IsElevated)
            {
                _logger?.LogWarning(
                    "SECURITY: Main application is running as root. " +
                    "This is not recommended - the application should run as standard user " +
                    "and use the helper process for elevated operations.");
            }

            _logger?.LogInformation("Starting elevated helper process");

            if (_helperManager != null)
            {
                var validationResult = _helperManager.ValidateHelper();
                if (!validationResult.IsValid)
                {
                    _logger?.LogError("Helper validation failed: {Error}", validationResult.ErrorMessage);
                    return ElevationResult.Failed($"Helper validation failed: {validationResult.ErrorMessage}");
                }

                var startResult = await _helperManager.StartAsync(cancellationToken);
                if (!startResult.IsSuccess)
                {
                    _logger?.LogWarning("Failed to start helper: {Status} - {Error}",
                        startResult.Status, startResult.ErrorMessage);

                    return startResult.Status switch
                    {
                        HelperStartStatus.UserCancelled => ElevationResult.Cancelled(),
                        HelperStartStatus.HelperNotFound => ElevationResult.Failed("Helper executable not found"),
                        HelperStartStatus.ValidationFailed => ElevationResult.Failed($"Validation failed: {startResult.ErrorMessage}"),
                        _ => ElevationResult.Failed(startResult.ErrorMessage ?? "Failed to start helper")
                    };
                }

                _logger?.LogInformation("Helper process started with PID {ProcessId}", startResult.ProcessId);
            }
            else
            {
                _logger?.LogInformation(
                    "Helper process manager not available. " +
                    "The minimal elevated helper is not yet implemented. " +
                    "See docs/DESIGN_PER_ADDRESS_TRACKING.md for the planned architecture.");
                return ElevationResult.NotSupported();
            }

            // Connect to the helper via Unix domain socket
            _helperConnection = await ConnectToHelperAsync(cancellationToken);
            if (_helperConnection == null || !_helperConnection.IsConnected)
            {
                _logger?.LogError("Failed to establish connection to helper process");
                return ElevationResult.Failed("Failed to connect to helper process");
            }

            _logger?.LogInformation("Successfully connected to elevated helper process");
            OnHelperConnectionStateChanged(true, "Helper started and connected");
            return ElevationResult.Success();
        }
        catch (OperationCanceledException)
        {
            _logger?.LogInformation("Helper start cancelled");
            return ElevationResult.Cancelled();
        }
        catch (Exception ex)
        {
            _logger?.LogError(ex, "Failed to start helper process");
            return ElevationResult.Failed($"Failed to start helper: {ex.Message}");
        }
        finally
        {
            _connectionLock.Release();
        }
    }

    /// <inheritdoc />
    public async Task StopHelperAsync()
    {
        await _connectionLock.WaitAsync();
        try
        {
            if (_helperConnection != null)
            {
                _logger?.LogInformation("Disconnecting from helper process");
                await _helperConnection.DisconnectAsync();
                await _helperConnection.DisposeAsync();
                _helperConnection = null;
            }

            if (_helperManager != null)
            {
                _logger?.LogInformation("Stopping helper process");
                await _helperManager.StopAsync(TimeSpan.FromSeconds(5));
            }

            OnHelperConnectionStateChanged(false, "Helper stopped");
        }
        catch (Exception ex)
        {
            _logger?.LogError(ex, "Error stopping helper process");
        }
        finally
        {
            _connectionLock.Release();
        }
    }

    /// <inheritdoc />
    public IHelperConnection? GetHelperConnection()
    {
        return _helperConnection?.IsConnected == true ? _helperConnection : null;
    }

    /// <inheritdoc />
    public bool RequiresElevationFor(ElevatedFeature feature)
    {
        return feature switch
        {
            // Per-process byte tracking requires helper for eBPF access
            ElevatedFeature.PerProcessNetworkMonitoring => !IsHelperConnected,
            // Raw socket capture always requires helper
            ElevatedFeature.RawSocketCapture => !IsHelperConnected,
            _ => false
        };
    }

    /// <summary>
    /// Connects to the helper process via Unix domain socket.
    /// </summary>
    private async Task<IHelperConnection?> ConnectToHelperAsync(CancellationToken cancellationToken)
    {
        // TODO: Implement actual Unix domain socket connection when helper is implemented
        // The connection would:
        // 1. Connect to socket: /run/wirebound/helper.sock
        // 2. Perform authentication handshake
        // 3. Establish session with timeout
        // 4. Return connected IHelperConnection

        _logger?.LogDebug("Helper connection not yet implemented");
        return null;
    }

    /// <summary>
    /// Checks if the current process is running as root.
    /// </summary>
    private static bool CheckIsElevated()
    {
        try
        {
            var uid = GetEffectiveUserId();
            return uid == 0;
        }
        catch
        {
            return false;
        }
    }

    /// <summary>
    /// Gets the effective user ID using /proc/self/status.
    /// </summary>
    private static int GetEffectiveUserId()
    {
        try
        {
            var lines = File.ReadAllLines("/proc/self/status");
            foreach (var line in lines)
            {
                if (line.StartsWith("Uid:", StringComparison.Ordinal))
                {
                    var parts = line.Split('\t', StringSplitOptions.RemoveEmptyEntries);
                    if (parts.Length >= 2 && int.TryParse(parts[1], out var euid))
                    {
                        return euid;
                    }
                }
            }
        }
        catch
        {
            if (string.Equals(Environment.UserName, "root", StringComparison.Ordinal))
            {
                return 0;
            }
        }

        return -1;
    }

    private void OnHelperConnectionStateChanged(bool isConnected, string reason)
    {
        HelperConnectionStateChanged?.Invoke(this, new HelperConnectionStateChangedEventArgs(isConnected, reason));
    }

    // === Legacy Methods (Full App Elevation) ===
    // These are for backwards compatibility. On Linux, full app elevation is not supported.

    /// <inheritdoc />
    public Task<ElevationResult> TryElevateAsync()
    {
        // Linux doesn't support full app elevation like Windows UAC
        // Use the helper process approach instead
        _logger?.LogInformation("Full app elevation not supported on Linux. Use helper process instead.");
        return Task.FromResult(ElevationResult.NotSupported());
    }

    /// <inheritdoc />
    public void ExitAfterElevation()
    {
        // No-op on Linux
        _logger?.LogWarning("ExitAfterElevation called on Linux - this is a no-op");
    }

    public async ValueTask DisposeAsync()
    {
        if (_disposed) return;
        _disposed = true;

        await StopHelperAsync();
        _connectionLock.Dispose();
    }
}

