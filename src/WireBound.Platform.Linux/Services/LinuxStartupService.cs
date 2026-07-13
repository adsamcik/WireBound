using System.Diagnostics;
using System.Runtime.Versioning;
using Serilog;
using WireBound.Platform.Abstract.Services;

namespace WireBound.Platform.Linux.Services;

/// <summary>
/// Linux-specific implementation of startup service using XDG autostart.
/// Creates a .desktop file in ~/.config/autostart/ which is the standard
/// way to register applications for autostart on Linux desktop environments.
/// </summary>
[SupportedOSPlatform("linux")]
public sealed class LinuxStartupService : IStartupService
{
    private const string DesktopFileName = "wirebound.desktop";
    private const string AppName = "WireBound";

    private const string HelperServiceName = "wirebound-elevation";
    private const string HelperExecutableName = "wirebound-elevation";

    // Path of the hardened system-level template unit installed by
    // LinuxHelperProcessManager.InstallService(). The Settings toggle only
    // enables/disables the per-user instance of this template; it never owns
    // installation/removal of the template itself.
    private const string SystemTemplatePath =
        $"/etc/systemd/system/{HelperServiceName}@.service";

    private readonly IHelperProcessManager? _helperManager;

    public LinuxStartupService(IHelperProcessManager? helperManager = null)
    {
        _helperManager = helperManager;
    }

    public bool IsStartupSupported => HasDesktopEnvironment();

    public Task<bool> IsStartupEnabledAsync()
    {
        try
        {
            var desktopFilePath = GetDesktopFilePath();
            if (!File.Exists(desktopFilePath))
                return Task.FromResult(false);

            // Check if Hidden=true is set (which disables the entry)
            var content = File.ReadAllText(desktopFilePath);
            if (content.Contains("Hidden=true", StringComparison.OrdinalIgnoreCase))
                return Task.FromResult(false);

            return Task.FromResult(true);
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
            var desktopFilePath = GetDesktopFilePath();
            var autostartDir = Path.GetDirectoryName(desktopFilePath)!;

            if (enable)
            {
                // Ensure autostart directory exists
                Directory.CreateDirectory(autostartDir);

                var exePath = Environment.ProcessPath;
                if (string.IsNullOrEmpty(exePath))
                {
                    Log.Error("Failed to get executable path");
                    return Task.FromResult(false);
                }

                // Create the .desktop file
                var desktopEntry = $"""
                    [Desktop Entry]
                    Type=Application
                    Version=1.5
                    Name={AppName}
                    GenericName=Network Monitor
                    Comment=Cross-platform network traffic monitoring
                    Exec="{exePath}" --minimized
                    Terminal=false
                    StartupNotify=false
                    Categories=Network;Monitor;System;
                    X-GNOME-Autostart-enabled=true
                    """;

                File.WriteAllText(desktopFilePath, desktopEntry);
                Log.Information("Added WireBound to Linux autostart");
            }
            else
            {
                // Delete the .desktop file
                if (File.Exists(desktopFilePath))
                {
                    File.Delete(desktopFilePath);
                    Log.Information("Removed WireBound from Linux autostart");
                }
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
            var desktopFilePath = GetDesktopFilePath();

            if (!File.Exists(desktopFilePath))
                return Task.FromResult(StartupState.Disabled);

            var content = File.ReadAllText(desktopFilePath);

            // Check if Hidden=true (disabled)
            if (content.Contains("Hidden=true", StringComparison.OrdinalIgnoreCase))
                return Task.FromResult(StartupState.DisabledByUser);

            // Check GNOME-specific disable flag
            if (content.Contains("X-GNOME-Autostart-enabled=false", StringComparison.OrdinalIgnoreCase))
                return Task.FromResult(StartupState.DisabledByUser);

            return Task.FromResult(StartupState.Enabled);
        }
        catch (Exception ex)
        {
            Log.Error(ex, "Failed to get startup state");
            return Task.FromResult(StartupState.Error);
        }
    }

    private static string GetDesktopFilePath()
    {
        // XDG_CONFIG_HOME or ~/.config
        var configDir = Environment.GetFolderPath(
            Environment.SpecialFolder.ApplicationData,
            Environment.SpecialFolderOption.DoNotVerify);

        if (string.IsNullOrEmpty(configDir))
        {
            var home = Environment.GetEnvironmentVariable("HOME")
                ?? Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
            configDir = Path.Combine(home, ".config");
        }

        return Path.Combine(configDir, "autostart", DesktopFileName);
    }

    private static bool HasDesktopEnvironment()
    {
        // Check for common desktop environment indicators
        return !string.IsNullOrEmpty(Environment.GetEnvironmentVariable("XDG_CURRENT_DESKTOP")) ||
               !string.IsNullOrEmpty(Environment.GetEnvironmentVariable("DESKTOP_SESSION")) ||
               !string.IsNullOrEmpty(Environment.GetEnvironmentVariable("DISPLAY")) ||
               !string.IsNullOrEmpty(Environment.GetEnvironmentVariable("WAYLAND_DISPLAY"));
    }

    public Task<bool> EnsureStartupPathUpdatedAsync()
    {
        try
        {
            var desktopFilePath = GetDesktopFilePath();
            if (!File.Exists(desktopFilePath))
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

            var content = File.ReadAllText(desktopFilePath);
            var expectedExecLine = $"Exec=\"{currentExePath}\" --minimized";

            // Check if the Exec line matches the current executable
            // Parse the Exec line from the desktop file
            var lines = content.Split('\n');
            var execLineIndex = -1;
            var currentExecLine = string.Empty;

            for (var i = 0; i < lines.Length; i++)
            {
                if (lines[i].StartsWith("Exec=", StringComparison.Ordinal))
                {
                    execLineIndex = i;
                    currentExecLine = lines[i].TrimEnd();
                    break;
                }
            }

            if (execLineIndex == -1)
            {
                Log.Warning("Desktop file exists but has no Exec line");
                return Task.FromResult(false);
            }

            if (!string.Equals(currentExecLine, expectedExecLine, StringComparison.Ordinal))
            {
                Log.Information(
                    "Updating autostart desktop file Exec line from '{OldExec}' to '{NewExec}'",
                    currentExecLine, expectedExecLine);

                lines[execLineIndex] = expectedExecLine;
                File.WriteAllText(desktopFilePath, string.Join('\n', lines));
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

    // === Helper Startup (system-level systemd template instance) ===
    //
    // The elevated helper needs root (CAP_NET_ADMIN for SOCK_DIAG / eBPF), so a
    // user-level unit is useless. Instead we delegate to the hardened system
    // template unit (/etc/systemd/system/wirebound-elevation@.service) that
    // LinuxHelperProcessManager installs, and toggle the per-user instance
    // (wirebound-elevation@<uid>.service) of that template.

    public bool IsHelperStartupSupported => true;

    public Task<bool> IsHelperStartupEnabledAsync()
    {
        try
        {
            var uid = LinuxHelperProcessManager.GetCurrentUid();
            var result = RunCommand(
                "systemctl", $"is-enabled --quiet {HelperServiceName}@{uid}.service");
            return Task.FromResult(result == 0);
        }
        catch (Exception ex)
        {
            Log.Error(ex, "Failed to check helper startup status");
            return Task.FromResult(false);
        }
    }

    public async Task<bool> SetHelperStartupEnabledAsync(bool enable)
    {
        if (_helperManager is null)
        {
            Log.Error("Cannot change helper auto-start: helper process manager is unavailable");
            return false;
        }

        try
        {
            var uid = LinuxHelperProcessManager.GetCurrentUid();
            var instance = $"{HelperServiceName}@{uid}.service";

            if (enable)
            {
                // Ensure the hardened system template + polkit policy are present.
                // This is a one-time pkexec password prompt — acceptable because the
                // user is explicitly opting in to elevated auto-start.
                if (!File.Exists(SystemTemplatePath))
                {
                    if (!await _helperManager.EnsureRegisteredAsync().ConfigureAwait(false))
                    {
                        Log.Error("Failed to install system elevation template unit");
                        return false;
                    }
                }

                // Enabling a system unit requires root, hence pkexec.
                var result = RunCommand(
                    "/usr/bin/pkexec", $"/usr/bin/systemctl enable {instance}");
                if (result == 0)
                {
                    Log.Information("Enabled helper auto-start via system unit {Instance}", instance);
                    return true;
                }

                Log.Error("Failed to enable helper system unit {Instance} (exit code: {Code})", instance, result);
                return false;
            }
            else
            {
                // Disable + stop the per-user instance. Do NOT uninstall the
                // template — other users on this machine may still rely on it.
                var disableResult = RunCommand(
                    "/usr/bin/pkexec", $"/usr/bin/systemctl disable {instance}");
                RunCommand("/usr/bin/pkexec", $"/usr/bin/systemctl stop {instance}");

                if (disableResult == 0)
                {
                    Log.Information("Disabled helper auto-start ({Instance})", instance);
                    return true;
                }

                Log.Warning("Failed to disable helper system unit {Instance} (exit code: {Code})", instance, disableResult);
                return false;
            }
        }
        catch (Exception ex)
        {
            Log.Error(ex, "Failed to set helper startup");
            return false;
        }
    }

    public async Task<bool> EnsureHelperStartupPathUpdatedAsync()
    {
        try
        {
            if (!File.Exists(SystemTemplatePath))
            {
                // Template not installed, nothing to update.
                return true;
            }

            var helperPath = Path.Combine(AppContext.BaseDirectory, HelperExecutableName);
            var content = File.ReadAllText(SystemTemplatePath);
            var expectedExecStart = $"ExecStart={helperPath}";

            if (content.Contains(expectedExecStart, StringComparison.Ordinal))
            {
                return true;
            }

            Log.Information("Helper system template ExecStart is stale; reinstalling for path: {Path}", helperPath);

            // The template lives under /etc and is root-owned, so a rewrite must
            // go through the manager's elevated install path.
            if (_helperManager is LinuxHelperProcessManager linuxManager)
            {
                return await linuxManager.InstallService().ConfigureAwait(false);
            }

            Log.Error("Helper startup path is stale but no Linux helper manager is available to repair it");
            return false;
        }
        catch (Exception ex)
        {
            Log.Error(ex, "Failed to ensure helper startup path is updated");
            return false;
        }
    }

    private static int RunCommand(string command, string arguments)
    {
        var startInfo = new ProcessStartInfo
        {
            FileName = command,
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
}
