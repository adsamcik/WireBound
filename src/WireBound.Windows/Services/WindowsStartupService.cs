using Microsoft.Extensions.Logging;
using System.ComponentModel;
using System.Diagnostics;
using WireBound.Core.Services;

namespace WireBound.Windows.Services;

/// <summary>
/// Windows implementation of startup service.
/// Handles both packaged (MSIX) and non-packaged (portable) deployments.
/// </summary>
public sealed class WindowsStartupService : IStartupService
{
    private const string AppName = "WireBound";
    private const string RegistryRunKey = @"SOFTWARE\Microsoft\Windows\CurrentVersion\Run";
    private const string StartupTaskId = "WireBoundStartupTask";
    
    private readonly ILogger<WindowsStartupService> _logger;
    private readonly IWindowsRegistryService _registryService;
    private readonly IWindowsStartupTaskService _startupTaskService;
    private readonly bool _isPackaged;

    public bool IsStartupSupported => true;

    public WindowsStartupService(
        ILogger<WindowsStartupService> logger,
        IWindowsRegistryService registryService,
        IWindowsStartupTaskService startupTaskService)
    {
        _logger = logger;
        _registryService = registryService;
        _startupTaskService = startupTaskService;
        _isPackaged = IsRunningAsPackaged();
        _logger.LogDebug("WindowsStartupService initialized. IsPackaged: {IsPackaged}", _isPackaged);
    }

    private static bool IsRunningAsPackaged()
    {
        try
        {
            var package = global::Windows.ApplicationModel.Package.Current;
            return package?.Id != null;
        }
        catch (InvalidOperationException)
        {
            return false;
        }
        catch
        {
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
            var success = state == WindowsStartupTaskState.Enabled ||
                          state == WindowsStartupTaskState.EnabledByPolicy;
            
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

    private static StartupState MapStartupTaskState(WindowsStartupTaskState state)
    {
        return state switch
        {
            WindowsStartupTaskState.Enabled => StartupState.Enabled,
            WindowsStartupTaskState.EnabledByPolicy => StartupState.Enabled,
            WindowsStartupTaskState.Disabled => StartupState.Disabled,
            WindowsStartupTaskState.DisabledByUser => StartupState.DisabledByUser,
            WindowsStartupTaskState.DisabledByPolicy => StartupState.DisabledByPolicy,
            WindowsStartupTaskState.NotFound => StartupState.NotSupported,
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

        try
        {
            var exePath = GetExecutablePath();
            var registeredPath = value.Trim('"');
            
            if (registeredPath.Equals(exePath, StringComparison.OrdinalIgnoreCase))
            {
                return StartupState.Enabled;
            }

            _logger.LogWarning("Stale startup registry entry detected. Cleaning up.");
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
    }

    private bool SetRegistryStartupEnabled(bool enable)
    {
        if (enable)
        {
            try
            {
                var exePath = GetExecutablePath();
                
                if (!File.Exists(exePath))
                {
                    _logger.LogError("Executable path does not exist: {Path}", exePath);
                    return false;
                }
                
                if (exePath.Contains('"'))
                {
                    _logger.LogError("Executable path contains invalid characters");
                    return false;
                }
                
                exePath = Path.GetFullPath(exePath);
                
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
        var processPath = Environment.ProcessPath;
        if (!string.IsNullOrEmpty(processPath))
        {
            return processPath;
        }

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
