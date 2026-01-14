using System.Diagnostics;
using System.Security.Principal;
using Microsoft.Extensions.Logging;
using WireBound.Core.Services;

namespace WireBound.Windows.Services;

/// <summary>
/// Windows-specific service for checking and requesting elevated (administrator) privileges.
/// </summary>
public sealed class WindowsElevationService : IElevationService
{
    private readonly ILogger<WindowsElevationService> _logger;
    private readonly Lazy<bool> _isElevated;

    public WindowsElevationService(ILogger<WindowsElevationService> logger)
    {
        _logger = logger;
        _isElevated = new Lazy<bool>(CheckElevation);
    }

    public bool IsElevated => _isElevated.Value;

    public async Task<bool> RequestElevationAsync()
    {
        if (IsElevated)
        {
            _logger.LogInformation("Already running with elevated privileges");
            return true;
        }

        try
        {
            _logger.LogInformation("Requesting elevation - will restart app with admin privileges");

            var exePath = GetExecutablePath();
            if (string.IsNullOrEmpty(exePath))
            {
                _logger.LogError("Could not determine executable path for elevation");
                return false;
            }

            var args = Environment.GetCommandLineArgs().Skip(1);
            var argsString = string.Join(" ", args.Select(a => a.Contains(' ') ? $"\"{a}\"" : a));

            var startInfo = new ProcessStartInfo
            {
                FileName = exePath,
                Arguments = argsString,
                UseShellExecute = true,
                Verb = "runas" // Triggers UAC elevation
            };

            _logger.LogDebug("Starting elevated process: {Path} {Args}", exePath, argsString);

            var process = Process.Start(startInfo);
            if (process != null)
            {
                _logger.LogInformation("Elevated process started, shutting down current instance");
                await Task.Delay(500);
                Microsoft.Maui.Controls.Application.Current?.Quit();
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
            _logger.LogInformation("User cancelled elevation request");
            return false;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to request elevation");
            return false;
        }
    }

    public bool RequiresElevationFor(ElevatedFeature feature)
    {
        if (IsElevated)
        {
            return false;
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
    }

    private static string? GetExecutablePath()
    {
        var processPath = Environment.ProcessPath;
        
        if (!string.IsNullOrEmpty(processPath))
        {
            return processPath;
        }

        try
        {
            return Process.GetCurrentProcess().MainModule?.FileName;
        }
        catch
        {
            return null;
        }
    }
}
