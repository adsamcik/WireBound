using System.Runtime.Versioning;
using Microsoft.Win32;
using Serilog;
using WireBound.Platform.Abstract.Services;

namespace WireBound.Platform.Windows.Services;

/// <summary>
/// Windows-specific implementation of startup service using the Registry.
/// This integrates with Windows Settings > Apps > Startup.
/// </summary>
[SupportedOSPlatform("windows")]
public sealed class WindowsStartupService : IStartupService
{
    private const string RegistryKeyPath = @"SOFTWARE\Microsoft\Windows\CurrentVersion\Run";
    private const string AppName = "WireBound";

    public bool IsStartupSupported => OperatingSystem.IsWindows();

    public Task<bool> IsStartupEnabledAsync()
    {
        try
        {
            using var key = Registry.CurrentUser.OpenSubKey(RegistryKeyPath, writable: false);
            var value = key?.GetValue(AppName);
            return Task.FromResult(value != null);
        }
        catch (Exception ex)
        {
            Log.Error(ex, "Failed to check startup status");
            return Task.FromResult(false);
        }
    }

    public Task<bool> SetStartupEnabledAsync(bool enable)
    {
        try
        {
            using var key = Registry.CurrentUser.OpenSubKey(RegistryKeyPath, writable: true);
            if (key == null)
            {
                Log.Error("Failed to open registry key for startup");
                return Task.FromResult(false);
            }

            if (enable)
            {
                var exePath = Environment.ProcessPath;
                if (string.IsNullOrEmpty(exePath))
                {
                    Log.Error("Failed to get executable path");
                    return Task.FromResult(false);
                }

                // Add the application to startup with the --minimized flag
                key.SetValue(AppName, $"\"{exePath}\" --minimized");
                Log.Information("Added WireBound to Windows startup");
            }
            else
            {
                key.DeleteValue(AppName, throwOnMissingValue: false);
                Log.Information("Removed WireBound from Windows startup");
            }

            return Task.FromResult(true);
        }
        catch (Exception ex)
        {
            Log.Error(ex, "Failed to set startup status");
            return Task.FromResult(false);
        }
    }

    public async Task<StartupResult> SetStartupWithResultAsync(bool enable)
    {
        var success = await SetStartupEnabledAsync(enable);
        var state = await GetStartupStateAsync();
        return success ? StartupResult.Succeeded(state) : StartupResult.Failed(state);
    }

    public Task<StartupState> GetStartupStateAsync()
    {
        if (!IsStartupSupported)
        {
            return Task.FromResult(StartupState.NotSupported);
        }

        try
        {
            // Check current user registry (this is what Windows Startup Apps shows)
            using var key = Registry.CurrentUser.OpenSubKey(RegistryKeyPath, writable: false);
            var value = key?.GetValue(AppName);

            if (value != null)
            {
                // Check if it's been disabled via the Approved key
                // Windows stores disabled startup items in a separate location
                using var approvedKey = Registry.CurrentUser.OpenSubKey(
                    @"SOFTWARE\Microsoft\Windows\CurrentVersion\Explorer\StartupApproved\Run", 
                    writable: false);
                
                var approvedValue = approvedKey?.GetValue(AppName) as byte[];
                if (approvedValue != null && approvedValue.Length >= 1)
                {
                    // If first byte is not 2, it's disabled
                    // 02 00 00 00 ... = enabled
                    // 03 00 00 00 ... = disabled by user
                    if (approvedValue[0] == 0x03)
                    {
                        return Task.FromResult(StartupState.DisabledByUser);
                    }
                    if (approvedValue[0] != 0x02 && approvedValue[0] != 0x00)
                    {
                        // Other values may indicate policy or other disabling
                        return Task.FromResult(StartupState.DisabledByPolicy);
                    }
                }

                return Task.FromResult(StartupState.Enabled);
            }

            return Task.FromResult(StartupState.Disabled);
        }
        catch (Exception ex)
        {
            Log.Error(ex, "Failed to get startup state");
            return Task.FromResult(StartupState.Error);
        }
    }
}
