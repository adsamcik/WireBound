using Microsoft.Extensions.Logging;
using System.ComponentModel;
using System.Diagnostics;
using WireBound.Services.Abstractions;
using StartupTaskStateAbstraction = WireBound.Services.Abstractions.StartupTaskState;

namespace WireBound.Services;

/// <summary>
/// Windows implementation of startup service.
/// Handles both packaged (MSIX) and non-packaged (portable) deployments.
/// 
/// IMPORTANT: The StartupTaskId must match the TaskId in Package.appxmanifest.
/// If you change one, you must change the other.
/// </summary>
public sealed class StartupService : IStartupService
{
    private const string AppName = "WireBound";
    private const string RegistryRunKey = @"SOFTWARE\Microsoft\Windows\CurrentVersion\Run";
    
    /// <summary>
    /// Must match TaskId in Platforms/Windows/Package.appxmanifest
    /// </summary>
    private const string StartupTaskId = "WireBoundStartupTask";
    
    private readonly ILogger<StartupService> _logger;
    private readonly IRegistryService _registryService;
    private readonly IStartupTaskService _startupTaskService;
    private readonly bool _isPackaged;  // Set once in constructor, never changes

    public bool IsStartupSupported => true;

    public StartupService(
        ILogger<StartupService> logger,
        IRegistryService registryService,
        IStartupTaskService startupTaskService)
    {
        _logger = logger;
        _registryService = registryService;
        _startupTaskService = startupTaskService;
        _isPackaged = IsRunningAsPackaged();
        _logger.LogDebug("StartupService initialized. IsPackaged: {IsPackaged}", _isPackaged);
    }

    /// <summary>
    /// Determines if the app is running as a packaged (MSIX) application.
    /// </summary>
    private static bool IsRunningAsPackaged()
    {
        try
        {
            // Try to access package identity - this will throw if not packaged
            var package = Windows.ApplicationModel.Package.Current;
            return package?.Id != null;
        }
        catch (InvalidOperationException)
        {
            // Expected when not running as a packaged app
            return false;
        }
        catch
        {
            // Other unexpected errors - assume not packaged
            return false;
        }
    }
    public async Task<bool> IsStartupEnabledAsync()
    {
        var state = await GetStartupStateAsync().ConfigureAwait(false);
        return state == StartupState.Enabled;
    }

    public async Task<StartupState> GetStartupStateAsync()
    {
        try
        {
            if (_isPackaged)
            {
                return await GetPackagedStartupStateAsync().ConfigureAwait(false);
            }
            else
            {
                return GetRegistryStartupState();
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error getting startup state");
            return StartupState.Error;
        }
    }

    public async Task<bool> SetStartupEnabledAsync(bool enable)
    {
        var result = await SetStartupWithResultAsync(enable).ConfigureAwait(false);
        return result.Success;
    }

    public async Task<StartupResult> SetStartupWithResultAsync(bool enable)
    {
        try
        {
            _logger.LogInformation("Setting startup enabled: {Enable}", enable);

            bool success;
            if (_isPackaged)
            {
                success = await SetPackagedStartupEnabledAsync(enable).ConfigureAwait(false);
            }
            else
            {
                success = SetRegistryStartupEnabled(enable);
            }

            // Get the resulting state
            var state = await GetStartupStateAsync().ConfigureAwait(false);
            
            return success 
                ? StartupResult.Succeeded(state) 
                : StartupResult.Failed(state);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error setting startup enabled to {Enable}", enable);
            return StartupResult.Failed(StartupState.Error);
        }
    }

    #region Packaged (MSIX) Implementation

    private async Task<StartupState> GetPackagedStartupStateAsync()
    {
        var state = await _startupTaskService.GetStateAsync(StartupTaskId).ConfigureAwait(false);
        return MapStartupTaskState(state);
    }

    private async Task<bool> SetPackagedStartupEnabledAsync(bool enable)
    {
        if (enable)
        {
            var state = await _startupTaskService.RequestEnableAsync(StartupTaskId).ConfigureAwait(false);
            var success = state == StartupTaskStateAbstraction.Enabled ||
                          state == StartupTaskStateAbstraction.EnabledByPolicy;
            
            if (!success)
            {
                _logger.LogWarning("Startup enable request returned state: {State}", state);
            }
            
            return success;
        }
        else
        {
            return await _startupTaskService.DisableAsync(StartupTaskId).ConfigureAwait(false);
        }
    }

    private static StartupState MapStartupTaskState(StartupTaskStateAbstraction state)
    {
        return state switch
        {
            StartupTaskStateAbstraction.Enabled => StartupState.Enabled,
            StartupTaskStateAbstraction.EnabledByPolicy => StartupState.Enabled,
            StartupTaskStateAbstraction.Disabled => StartupState.Disabled,
            StartupTaskStateAbstraction.DisabledByUser => StartupState.DisabledByUser,
            StartupTaskStateAbstraction.DisabledByPolicy => StartupState.DisabledByPolicy,
            StartupTaskStateAbstraction.NotFound => StartupState.NotSupported,
            _ => StartupState.Error
        };
    }

    #endregion

    #region Non-Packaged (Portable) Implementation

    private StartupState GetRegistryStartupState()
    {
        var value = _registryService.GetValue(RegistryRunKey, AppName);

        if (string.IsNullOrEmpty(value))
        {
            return StartupState.Disabled;
        }

        // Verify the path still matches current executable location
        try
        {
            var exePath = GetExecutablePath();
            var registeredPath = value.Trim('"');
            
            if (registeredPath.Equals(exePath, StringComparison.OrdinalIgnoreCase))
            {
                return StartupState.Enabled;
            }

            // Stale registry entry - app was moved. Clean it up.
            _logger.LogWarning("Stale startup registry entry detected. App moved from '{OldPath}' to '{NewPath}'. Cleaning up.", 
                registeredPath, exePath);
            CleanupStaleRegistryEntry();
            return StartupState.Disabled;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error verifying registry startup state");
            return StartupState.Error;
        }
    }

    private void CleanupStaleRegistryEntry()
    {
        if (!_registryService.DeleteValue(RegistryRunKey, AppName))
        {
            _logger.LogWarning("Failed to clean up stale startup registry entry");
        }
        else
        {
            _logger.LogInformation("Cleaned up stale startup registry entry");
        }
    }

    private bool SetRegistryStartupEnabled(bool enable)
    {
        if (enable)
        {
            try
            {
                var exePath = GetExecutablePath();
                
                // Validate the executable path
                if (!File.Exists(exePath))
                {
                    _logger.LogError("Executable path does not exist: {Path}", exePath);
                    return false;
                }
                
                // Validate path doesn't contain characters that could cause issues
                if (exePath.Contains('"'))
                {
                    _logger.LogError("Executable path contains invalid characters: {Path}", exePath);
                    return false;
                }
                
                // Canonicalize the path to resolve any relative components
                exePath = Path.GetFullPath(exePath);
                
                // Quote the path to handle spaces
                var success = _registryService.SetValue(RegistryRunKey, AppName, $"\"{exePath}\"");
                if (success)
                {
                    _logger.LogInformation("Registered startup entry: {Path}", exePath);
                }
                return success;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error enabling registry startup");
                return false;
            }
        }
        else
        {
            var success = _registryService.DeleteValue(RegistryRunKey, AppName);
            if (success)
            {
                _logger.LogInformation("Removed startup entry");
            }
            return success;
        }
    }

    private static string GetExecutablePath()
    {
        // Get the main executable path
        var processPath = Environment.ProcessPath;
        if (!string.IsNullOrEmpty(processPath))
        {
            return processPath;
        }

        // Fallback to current process
        try
        {
            using var process = Process.GetCurrentProcess();
            return process.MainModule?.FileName 
                ?? throw new InvalidOperationException("Cannot determine executable path");
        }
        catch (Win32Exception ex)
        {
            throw new InvalidOperationException("Cannot access process module", ex);
        }
    }

    #endregion
}
