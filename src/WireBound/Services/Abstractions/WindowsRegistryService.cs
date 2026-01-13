using Microsoft.Extensions.Logging;
using Microsoft.Win32;
using System.Security;

namespace WireBound.Services.Abstractions;

/// <summary>
/// Windows Registry implementation for HKCU operations.
/// </summary>
public sealed class WindowsRegistryService : IRegistryService
{
    private readonly ILogger<WindowsRegistryService> _logger;

    public WindowsRegistryService(ILogger<WindowsRegistryService> logger)
    {
        _logger = logger;
    }

    public string? GetValue(string keyPath, string valueName)
    {
        try
        {
            using var key = Registry.CurrentUser.OpenSubKey(keyPath, false);
            return key?.GetValue(valueName) as string;
        }
        catch (SecurityException ex)
        {
            _logger.LogWarning(ex, "Access denied reading registry key: {KeyPath}", keyPath);
            return null;
        }
        catch (UnauthorizedAccessException ex)
        {
            _logger.LogWarning(ex, "Unauthorized access reading registry key: {KeyPath}", keyPath);
            return null;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error reading registry value: {KeyPath}\\{ValueName}", keyPath, valueName);
            return null;
        }
    }

    public bool SetValue(string keyPath, string valueName, string value)
    {
        try
        {
            using var key = Registry.CurrentUser.OpenSubKey(keyPath, true) 
                ?? Registry.CurrentUser.CreateSubKey(keyPath);
            
            if (key == null)
            {
                _logger.LogError("Cannot open or create registry key: {KeyPath}", keyPath);
                return false;
            }

            key.SetValue(valueName, value);
            _logger.LogDebug("Set registry value: {KeyPath}\\{ValueName}", keyPath, valueName);
            return true;
        }
        catch (SecurityException ex)
        {
            _logger.LogWarning(ex, "Access denied setting registry value: {KeyPath}\\{ValueName}", keyPath, valueName);
            return false;
        }
        catch (UnauthorizedAccessException ex)
        {
            _logger.LogWarning(ex, "Unauthorized access setting registry value: {KeyPath}\\{ValueName}", keyPath, valueName);
            return false;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error setting registry value: {KeyPath}\\{ValueName}", keyPath, valueName);
            return false;
        }
    }

    public bool DeleteValue(string keyPath, string valueName)
    {
        try
        {
            using var key = Registry.CurrentUser.OpenSubKey(keyPath, true);
            if (key == null)
            {
                // Key doesn't exist, value effectively deleted
                return true;
            }

            key.DeleteValue(valueName, throwOnMissingValue: false);
            _logger.LogDebug("Deleted registry value: {KeyPath}\\{ValueName}", keyPath, valueName);
            return true;
        }
        catch (SecurityException ex)
        {
            _logger.LogWarning(ex, "Access denied deleting registry value: {KeyPath}\\{ValueName}", keyPath, valueName);
            return false;
        }
        catch (UnauthorizedAccessException ex)
        {
            _logger.LogWarning(ex, "Unauthorized access deleting registry value: {KeyPath}\\{ValueName}", keyPath, valueName);
            return false;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error deleting registry value: {KeyPath}\\{ValueName}", keyPath, valueName);
            return false;
        }
    }
}
