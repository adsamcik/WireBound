using System.Diagnostics;
using System.Runtime.Versioning;
using System.Security.Principal;
using Microsoft.Extensions.Logging;
using WireBound.Platform.Abstract.Services;

namespace WireBound.Platform.Windows.Services;

/// <summary>
/// Windows implementation of <see cref="IElevationService"/>.
/// </summary>
/// <remarks>
/// <para>
/// This implementation uses a minimal elevated helper process pattern.
/// The main UI application NEVER runs elevated. Instead, a separate
/// helper process (WireBound.Helper.exe) handles all elevated operations.
/// </para>
/// <para>
/// <b>Security Architecture:</b>
/// <list type="bullet">
/// <item>Main UI runs with standard user privileges at all times</item>
/// <item>Helper is a minimal process that ONLY provides ETW data collection</item>
/// <item>Helper validates that connecting client is the legitimate WireBound app</item>
/// <item>Communication via named pipes with HMAC authentication</item>
/// <item>Sessions are time-limited (8 hour max)</item>
/// <item>All operations are logged for security auditing</item>
/// </list>
/// </para>
/// <para>
/// <b>Helper Process Constraints:</b>
/// The helper process is intentionally constrained:
/// <list type="bullet">
/// <item>No file system write access</item>
/// <item>No network access (except local IPC)</item>
/// <item>No ability to start other processes</item>
/// <item>No UI or user interaction capability</item>
/// <item>Read-only access to ETW network events</item>
/// </list>
/// </para>
/// </remarks>
[SupportedOSPlatform("windows")]
public sealed class WindowsElevationService : IElevationService, IAsyncDisposable
{
    private readonly ILogger<WindowsElevationService>? _logger;
    private readonly IHelperProcessManager? _helperManager;
    private readonly Lazy<bool> _isElevated;
    private IHelperConnection? _helperConnection;
    private readonly SemaphoreSlim _connectionLock = new(1, 1);
    private bool _disposed;

    /// <summary>
    /// Helper executable name.
    /// </summary>
    private const string HelperExecutableName = "WireBound.Helper.exe";

    public WindowsElevationService(
        ILogger<WindowsElevationService>? logger = null,
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
    public bool IsElevationSupported => true;

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

            // Log if running elevated - this is a security warning
            if (IsElevated)
            {
                _logger?.LogWarning(
                    "SECURITY: Main application is running with elevated privileges. " +
                    "This is not recommended - the application should run as standard user " +
                    "and use the helper process for elevated operations.");
            }

            _logger?.LogInformation("Starting elevated helper process");

            // Validate helper executable before launching
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
                // No helper manager - helper process not yet implemented
                _logger?.LogInformation(
                    "Helper process manager not available. " +
                    "The minimal elevated helper is not yet implemented. " +
                    "See docs/DESIGN_PER_ADDRESS_TRACKING.md for the planned architecture.");
                return ElevationResult.NotSupported();
            }

            // Connect to the helper
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
            // Per-process byte tracking requires helper for ETW access
            ElevatedFeature.PerProcessNetworkMonitoring => !IsHelperConnected,
            // Raw socket capture always requires helper
            ElevatedFeature.RawSocketCapture => !IsHelperConnected,
            _ => false
        };
    }

    /// <summary>
    /// Connects to the helper process via named pipe.
    /// </summary>
    private async Task<IHelperConnection?> ConnectToHelperAsync(CancellationToken cancellationToken)
    {
        // TODO: Implement actual named pipe connection when helper is implemented
        // The connection would:
        // 1. Connect to named pipe: \\.\pipe\WireBound.Helper
        // 2. Perform authentication handshake with HMAC signature
        // 3. Establish session with timeout
        // 4. Return connected IHelperConnection

        _logger?.LogDebug("Helper connection not yet implemented");
        return null;
    }

    /// <summary>
    /// Checks if the current process is running with administrator privileges.
    /// </summary>
    private static bool CheckIsElevated()
    {
        try
        {
            using var identity = WindowsIdentity.GetCurrent();
            var principal = new WindowsPrincipal(identity);
            return principal.IsInRole(WindowsBuiltInRole.Administrator);
        }
        catch
        {
            // If we can't determine elevation status, assume not elevated
            return false;
        }
    }

    private void OnHelperConnectionStateChanged(bool isConnected, string reason)
    {
        HelperConnectionStateChanged?.Invoke(this, new HelperConnectionStateChangedEventArgs(isConnected, reason));
    }

    // === Legacy Methods (Full App Elevation) ===
    // These are for backwards compatibility and will be deprecated when helper is implemented.

    /// <inheritdoc />
    public async Task<ElevationResult> TryElevateAsync()
    {
        // For now, delegate to the helper-based approach
        // In the future when helper is fully implemented, this method will be deprecated
        if (!IsElevationSupported)
        {
            return ElevationResult.NotSupported();
        }

        if (IsElevated)
        {
            _logger?.LogDebug("Already running with elevated privileges");
            return ElevationResult.Success();
        }

        try
        {
            // Start a new process with elevation
            var startInfo = new ProcessStartInfo
            {
                FileName = Environment.ProcessPath ?? Process.GetCurrentProcess().MainModule?.FileName,
                UseShellExecute = true,
                Verb = "runas"
            };

            if (string.IsNullOrEmpty(startInfo.FileName))
            {
                return ElevationResult.Failed("Could not determine process path");
            }

            _logger?.LogInformation("Attempting to restart with elevated privileges: {Path}", startInfo.FileName);
            
            var process = Process.Start(startInfo);
            if (process != null)
            {
                _logger?.LogInformation("Elevated process started with PID {Pid}", process.Id);
                return ElevationResult.Success();
            }

            return ElevationResult.Failed("Failed to start elevated process");
        }
        catch (System.ComponentModel.Win32Exception ex) when (ex.NativeErrorCode == 1223) // ERROR_CANCELLED
        {
            _logger?.LogInformation("User cancelled UAC prompt");
            return ElevationResult.Cancelled();
        }
        catch (Exception ex)
        {
            _logger?.LogError(ex, "Failed to elevate");
            return ElevationResult.Failed(ex.Message);
        }
    }

    /// <inheritdoc />
    public void ExitAfterElevation()
    {
        _logger?.LogInformation("Exiting after successful elevation");
        Environment.Exit(0);
    }

    public async ValueTask DisposeAsync()
    {
        if (_disposed) return;
        _disposed = true;

        await StopHelperAsync();
        _connectionLock.Dispose();
    }
}

