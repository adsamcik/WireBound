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
}
