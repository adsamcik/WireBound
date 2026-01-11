using System.Diagnostics;
using System.Security.Principal;
using Microsoft.Extensions.Logging;

namespace WireBound.Services;

/// <summary>
/// Windows-specific service for checking and requesting elevated (administrator) privileges.
/// </summary>
public sealed class ElevationService : IElevationService
{
    private readonly ILogger<ElevationService> _logger;
    private readonly Lazy<bool> _isElevated;

    public ElevationService(ILogger<ElevationService> logger)
    {
        _logger = logger;
        _isElevated = new Lazy<bool>(CheckElevation);
    }

    /// <inheritdoc />
    public bool IsElevated => _isElevated.Value;

    /// <inheritdoc />
    public async Task<bool> RequestElevationAsync()
    {
        if (IsElevated)
        {
            _logger.LogInformation("Already running with elevated privileges");
            return true;
        }

#if WINDOWS
        try
        {
            _logger.LogInformation("Requesting elevation - will restart app with admin privileges");

            // Get the current executable path
            var exePath = GetExecutablePath();
            if (string.IsNullOrEmpty(exePath))
            {
                _logger.LogError("Could not determine executable path for elevation");
                return false;
            }

            // Get current command line arguments (excluding the executable itself)
            var args = Environment.GetCommandLineArgs().Skip(1);
            var argsString = string.Join(" ", args.Select(a => a.Contains(' ') ? $"\"{a}\"" : a));

            var startInfo = new ProcessStartInfo
            {
                FileName = exePath,
                Arguments = argsString,
                UseShellExecute = true,
                Verb = "runas" // This triggers UAC elevation
            };

            _logger.LogDebug("Starting elevated process: {Path} {Args}", exePath, argsString);

            var process = Process.Start(startInfo);
            if (process != null)
            {
                _logger.LogInformation("Elevated process started, shutting down current instance");
                
                // Give the new process a moment to start
                await Task.Delay(500);
                
                // Shut down the current (non-elevated) instance
                Application.Current?.Quit();
                return true;
            }
            else
            {
                _logger.LogWarning("Failed to start elevated process");
                return false;
            }
        }
        catch (System.ComponentModel.Win32Exception ex) when (ex.NativeErrorCode == 1223)
        {
            // ERROR_CANCELLED - User declined UAC prompt
            _logger.LogInformation("User cancelled elevation request");
            return false;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to request elevation");
            return false;
        }
#else
        _logger.LogWarning("Elevation is only supported on Windows");
        await Task.CompletedTask;
        return false;
#endif
    }

    /// <inheritdoc />
    public bool RequiresElevationFor(ElevatedFeature feature)
    {
        if (IsElevated)
        {
            return false; // Already elevated, nothing requires elevation
        }

        return feature switch
        {
            ElevatedFeature.PerProcessNetworkMonitoring => true,
            ElevatedFeature.RawSocketCapture => true,
            _ => false
        };
    }

    private bool CheckElevation()
    {
#if WINDOWS
        try
        {
            using var identity = WindowsIdentity.GetCurrent();
            var principal = new WindowsPrincipal(identity);
            var isAdmin = principal.IsInRole(WindowsBuiltInRole.Administrator);
            
            _logger.LogInformation("Elevation check: Running as administrator = {IsAdmin}", isAdmin);
            return isAdmin;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to check elevation status");
            return false;
        }
#else
        return false;
#endif
    }

    private static string? GetExecutablePath()
    {
#if WINDOWS
        // For packaged MAUI apps, we need to get the correct executable
        var processPath = Environment.ProcessPath;
        
        if (!string.IsNullOrEmpty(processPath))
        {
            return processPath;
        }

        // Fallback to current process main module
        try
        {
            return Process.GetCurrentProcess().MainModule?.FileName;
        }
        catch
        {
            return null;
        }
#else
        return null;
#endif
    }
}
