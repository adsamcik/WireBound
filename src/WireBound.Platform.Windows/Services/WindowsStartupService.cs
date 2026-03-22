using System.Diagnostics;
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

    private const string HelperTaskName = "WireBound Elevation Helper";
    private const string HelperTaskFolder = @"\WireBound";
    private const string HelperTaskFullName = $@"{HelperTaskFolder}\{HelperTaskName}";
    private const string HelperExecutableName = "WireBound.Elevation.exe";

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

    public Task<bool> EnsureStartupPathUpdatedAsync()
    {
        try
        {
            using var key = Registry.CurrentUser.OpenSubKey(RegistryKeyPath, writable: true);
            if (key == null)
            {
                Log.Warning("Could not open registry key for startup path validation");
                return Task.FromResult(false);
            }

            var currentValue = key.GetValue(AppName) as string;
            if (string.IsNullOrEmpty(currentValue))
            {
                // Startup not enabled, nothing to update
                return Task.FromResult(true);
            }

            var currentExePath = Environment.ProcessPath;
            if (string.IsNullOrEmpty(currentExePath))
            {
                Log.Warning("Could not determine current executable path");
                return Task.FromResult(false);
            }

            var expectedValue = $"\"{currentExePath}\" --minimized";

            // Check if the current registry value matches the expected value
            if (!string.Equals(currentValue, expectedValue, StringComparison.OrdinalIgnoreCase))
            {
                Log.Information(
                    "Updating startup registry entry from '{OldPath}' to '{NewPath}'",
                    currentValue, expectedValue);

                key.SetValue(AppName, expectedValue);
                Log.Information("Startup path updated successfully");
            }

            return Task.FromResult(true);
        }
        catch (Exception ex)
        {
            Log.Error(ex, "Failed to ensure startup path is updated");
            return Task.FromResult(false);
        }
    }

    // === Helper Startup (Task Scheduler) ===

    public bool IsHelperStartupSupported => true;

    public Task<bool> IsHelperStartupEnabledAsync()
    {
        try
        {
            var result = RunSchtasks($"/Query /TN \"{HelperTaskFullName}\"");
            return Task.FromResult(result == 0);
        }
        catch (Exception ex)
        {
            Log.Error(ex, "Failed to check helper startup status");
            return Task.FromResult(false);
        }
    }

    public Task<bool> SetHelperStartupEnabledAsync(bool enable)
    {
        try
        {
            if (enable)
            {
                var helperPath = GetHelperPath();
                if (string.IsNullOrEmpty(helperPath) || !File.Exists(helperPath))
                {
                    Log.Error("Helper executable not found at: {Path}", helperPath);
                    return Task.FromResult(false);
                }

                var sid = System.Security.Principal.WindowsIdentity.GetCurrent().User!.Value;

                var args = $"/Create /TN \"{HelperTaskFullName}\" " +
                           $"/TR \"\\\"{helperPath}\\\" --caller-sid {sid}\" " +
                           "/SC ONLOGON " +
                           "/RL HIGHEST " +
                           "/F " +
                           "/NP";

                var result = RunSchtasksElevated(args);
                if (result == 0)
                {
                    Log.Information("Registered helper for auto-start via Task Scheduler");
                    return Task.FromResult(true);
                }

                Log.Error("Failed to register helper scheduled task (exit code: {Code})", result);
                return Task.FromResult(false);
            }
            else
            {
                var result = RunSchtasksElevated($"/Delete /TN \"{HelperTaskFullName}\" /F");
                if (result == 0)
                {
                    Log.Information("Unregistered helper from auto-start");
                    return Task.FromResult(true);
                }

                Log.Error("Failed to unregister helper scheduled task (exit code: {Code})", result);
                return Task.FromResult(false);
            }
        }
        catch (System.ComponentModel.Win32Exception ex) when (ex.NativeErrorCode == 1223)
        {
            Log.Information("User cancelled elevation prompt for helper startup registration");
            return Task.FromResult(false);
        }
        catch (Exception ex)
        {
            Log.Error(ex, "Failed to set helper startup");
            return Task.FromResult(false);
        }
    }

    public Task<bool> EnsureHelperStartupPathUpdatedAsync()
    {
        try
        {
            // Check if the task exists first
            if (RunSchtasks($"/Query /TN \"{HelperTaskFullName}\"") != 0)
            {
                // Task not registered, nothing to update
                return Task.FromResult(true);
            }

            var helperPath = GetHelperPath();
            if (string.IsNullOrEmpty(helperPath))
            {
                Log.Warning("Could not determine helper executable path");
                return Task.FromResult(false);
            }

            // Query the task XML to check the current command path
            var taskXml = RunSchtasksWithOutput($"/Query /TN \"{HelperTaskFullName}\" /XML");
            if (string.IsNullOrEmpty(taskXml))
            {
                Log.Warning("Could not read helper scheduled task XML");
                return Task.FromResult(false);
            }

            // Check if the current command matches the expected helper path
            if (taskXml.Contains(helperPath, StringComparison.OrdinalIgnoreCase))
            {
                return Task.FromResult(true);
            }

            Log.Information("Updating helper scheduled task path to: {Path}", helperPath);

            // Re-register the task with the updated path (triggers UAC)
            var sid = System.Security.Principal.WindowsIdentity.GetCurrent().User!.Value;
            var args = $"/Create /TN \"{HelperTaskFullName}\" " +
                       $"/TR \"\\\"{helperPath}\\\" --caller-sid {sid}\" " +
                       "/SC ONLOGON " +
                       "/RL HIGHEST " +
                       "/F " +
                       "/NP";

            var result = RunSchtasksElevated(args);
            if (result == 0)
            {
                Log.Information("Helper scheduled task path updated successfully");
                return Task.FromResult(true);
            }

            Log.Error("Failed to update helper scheduled task (exit code: {Code})", result);
            return Task.FromResult(false);
        }
        catch (System.ComponentModel.Win32Exception ex) when (ex.NativeErrorCode == 1223)
        {
            Log.Information("User cancelled elevation prompt for helper startup path update");
            return Task.FromResult(false);
        }
        catch (Exception ex)
        {
            Log.Error(ex, "Failed to ensure helper startup path is updated");
            return Task.FromResult(false);
        }
    }

    private static string GetHelperPath() =>
        Path.Combine(AppContext.BaseDirectory, HelperExecutableName);

    private static int RunSchtasks(string arguments)
    {
        var startInfo = new ProcessStartInfo
        {
            FileName = "schtasks.exe",
            Arguments = arguments,
            UseShellExecute = false,
            CreateNoWindow = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true
        };

        using var process = Process.Start(startInfo)!;
        process.WaitForExit(10000);
        return process.ExitCode;
    }

    private static string? RunSchtasksWithOutput(string arguments)
    {
        var startInfo = new ProcessStartInfo
        {
            FileName = "schtasks.exe",
            Arguments = arguments,
            UseShellExecute = false,
            CreateNoWindow = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true
        };

        using var process = Process.Start(startInfo)!;
        var output = process.StandardOutput.ReadToEnd();
        process.WaitForExit(10000);
        return process.ExitCode == 0 ? output : null;
    }

    /// <summary>
    /// Runs schtasks.exe with elevation (triggers UAC prompt).
    /// </summary>
    private static int RunSchtasksElevated(string arguments)
    {
        var startInfo = new ProcessStartInfo
        {
            FileName = "schtasks.exe",
            Arguments = arguments,
            UseShellExecute = true,
            Verb = "runas",
            WindowStyle = ProcessWindowStyle.Hidden
        };

        using var process = Process.Start(startInfo);
        if (process == null)
            return -1;

        process.WaitForExit(30000);
        return process.ExitCode;
    }
}
