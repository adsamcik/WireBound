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
/// This implementation uses the Windows UAC (User Account Control) mechanism
/// to request elevation by restarting the application with the "runas" verb.
/// </para>
/// <para>
/// <b>Security Considerations:</b>
/// <list type="bullet">
/// <item>Validates that <see cref="Environment.ProcessPath"/> is not null/empty</item>
/// <item>Validates that the process path exists and is a file (not directory)</item>
/// <item>All elevation attempts are logged for audit purposes</item>
/// <item>Does not auto-exit - caller controls the exit timing</item>
/// </list>
/// </para>
/// <para>
/// <b>Future Work:</b>
/// The ideal pattern is to use a separate elevated helper process instead of
/// elevating the entire application. See docs/DESIGN_PER_ADDRESS_TRACKING.md
/// for the WireBound.Helper architecture that would provide this capability.
/// </para>
/// </remarks>
[SupportedOSPlatform("windows")]
public sealed class WindowsElevationService : IElevationService
{
    private readonly ILogger<WindowsElevationService>? _logger;
    private readonly Lazy<bool> _isElevated;

    public WindowsElevationService(ILogger<WindowsElevationService>? logger = null)
    {
        _logger = logger;
        _isElevated = new Lazy<bool>(CheckIsElevated);
    }

    /// <inheritdoc />
    public bool IsElevated => _isElevated.Value;

    /// <inheritdoc />
    public bool RequiresElevation => !IsElevated;

    /// <inheritdoc />
    public bool IsElevationSupported => true;

    /// <inheritdoc />
    public async Task<ElevationResult> TryElevateAsync()
    {
        _logger?.LogInformation("Elevation requested for WireBound application");

        // Validate process path before attempting elevation
        var validationResult = ValidateProcessPath();
        if (!validationResult.IsSuccess)
        {
            _logger?.LogError("Elevation blocked: {Error}", validationResult.ErrorMessage);
            return validationResult;
        }

        try
        {
            var processPath = Environment.ProcessPath!;
            _logger?.LogInformation("Starting elevated process: {ProcessPath}", processPath);

            var startInfo = new ProcessStartInfo
            {
                FileName = processPath,
                UseShellExecute = true,
                Verb = "runas",
                // Pass current working directory to elevated process
                WorkingDirectory = Environment.CurrentDirectory
            };

            var process = Process.Start(startInfo);
            
            if (process == null)
            {
                _logger?.LogError("Failed to start elevated process - Process.Start returned null");
                return ElevationResult.Failed("Failed to start elevated process");
            }

            _logger?.LogInformation(
                "Elevated process started successfully with PID {ProcessId}",
                process.Id);

            return ElevationResult.Success();
        }
        catch (System.ComponentModel.Win32Exception ex) when (ex.NativeErrorCode == 1223)
        {
            // ERROR_CANCELLED (1223) - The operation was canceled by the user
            _logger?.LogInformation("Elevation cancelled by user (UAC prompt dismissed)");
            return ElevationResult.Cancelled();
        }
        catch (Exception ex)
        {
            _logger?.LogError(ex, "Failed to elevate application");
            return ElevationResult.Failed($"Elevation failed: {ex.Message}");
        }
    }

    /// <inheritdoc />
    public void ExitAfterElevation()
    {
        _logger?.LogInformation("Exiting current process after successful elevation");
        Environment.Exit(0);
    }

    /// <inheritdoc />
    public bool RequiresElevationFor(ElevatedFeature feature)
    {
        return feature switch
        {
            // Per-process byte tracking requires elevation for ETW access
            ElevatedFeature.PerProcessNetworkMonitoring => !IsElevated,
            // Raw socket capture always requires elevation
            ElevatedFeature.RawSocketCapture => !IsElevated,
            _ => false
        };
    }

    /// <summary>
    /// Validates that the process path is safe to use for elevation.
    /// </summary>
    private static ElevationResult ValidateProcessPath()
    {
        var processPath = Environment.ProcessPath;

        // Check for null/empty path
        if (string.IsNullOrWhiteSpace(processPath))
        {
            return ElevationResult.Failed(
                "Cannot determine process path. Environment.ProcessPath is null or empty.");
        }

        // Check that the path points to an existing file
        if (!File.Exists(processPath))
        {
            return ElevationResult.Failed(
                $"Process path does not exist or is not a file: {processPath}");
        }

        // Check that it's an executable (basic sanity check)
        var extension = Path.GetExtension(processPath);
        if (!string.Equals(extension, ".exe", StringComparison.OrdinalIgnoreCase) &&
            !string.Equals(extension, ".dll", StringComparison.OrdinalIgnoreCase))
        {
            return ElevationResult.Failed(
                $"Process path does not appear to be an executable: {processPath}");
        }

        return ElevationResult.Success();
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
}
