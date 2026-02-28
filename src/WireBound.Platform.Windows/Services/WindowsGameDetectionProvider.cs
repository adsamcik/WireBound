using System.Runtime.Versioning;
using System.Text.Json;
using Microsoft.Extensions.Logging;
using Microsoft.Win32;
using WireBound.Platform.Abstract.Services;

namespace WireBound.Platform.Windows.Services;

/// <summary>
/// Windows game detection using GameConfigStore registry, Epic/GOG/Ubisoft/EA
/// launcher data. Builds a cached set of known game paths and install directories
/// for fast lookup during process categorization.
/// </summary>
[SupportedOSPlatform("windows")]
public sealed class WindowsGameDetectionProvider : IGameDetectionProvider
{
    private readonly ILogger<WindowsGameDetectionProvider>? _logger;

    // Known game executable paths from GameConfigStore (exact match)
    private HashSet<string> _knownGamePaths = new(StringComparer.OrdinalIgnoreCase);

    // Known game install directories from launcher registries/manifests (prefix match)
    private List<string> _knownGameDirectories = [];

    // Executables that GameConfigStore sometimes misidentifies as games
    private static readonly HashSet<string> GameConfigStoreFalsePositives =
        new(StringComparer.OrdinalIgnoreCase)
        {
            "chrome.exe", "firefox.exe", "msedge.exe", "opera.exe", "brave.exe",
            "vivaldi.exe", "chromium.exe",
            "blender.exe", "photoshop.exe", "premiere.exe", "afterfx.exe",
            "obs64.exe", "obs.exe",
            "explorer.exe", "dwm.exe", "taskmgr.exe",
            "code.exe", "devenv.exe",
        };

    public WindowsGameDetectionProvider(ILogger<WindowsGameDetectionProvider>? logger = null)
    {
        _logger = logger;
    }

    public bool IsKnownGame(string executablePath)
    {
        if (string.IsNullOrWhiteSpace(executablePath))
            return false;

        // Check exact path match (GameConfigStore entries)
        if (_knownGamePaths.Contains(executablePath))
            return true;

        // Check directory prefix match (launcher install directories)
        var directories = _knownGameDirectories;
        foreach (var dir in directories)
        {
            if (executablePath.StartsWith(dir, StringComparison.OrdinalIgnoreCase))
                return true;
        }

        return false;
    }

    public Task InitializeAsync(CancellationToken cancellationToken = default)
    {
        var paths = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var directories = new List<string>();

        ScanGameConfigStore(paths);
        ScanEpicGamesManifests(directories);
        ScanGogRegistry(directories);
        ScanUbisoftRegistry(directories);
        ScanEaRegistry(directories);

        // Atomic swap for thread safety
        _knownGamePaths = paths;
        _knownGameDirectories = directories;

        _logger?.LogInformation(
            "Game detection initialized: {PathCount} known game paths, {DirCount} known game directories",
            paths.Count, directories.Count);

        return Task.CompletedTask;
    }

    /// <summary>
    /// Scans Windows GameConfigStore for executables that Windows Game Mode has recognized as games.
    /// Path: HKCU\System\GameConfigStore\Children\{GUID}\MatchedExeFullPath
    /// </summary>
    private void ScanGameConfigStore(HashSet<string> paths)
    {
        try
        {
            using var key = Registry.CurrentUser.OpenSubKey(@"System\GameConfigStore\Children");
            if (key is null) return;

            var count = 0;
            foreach (var subKeyName in key.GetSubKeyNames())
            {
                using var gameKey = key.OpenSubKey(subKeyName);
                if (gameKey?.GetValue("MatchedExeFullPath") is not string exePath)
                    continue;

                var fileName = Path.GetFileName(exePath);
                if (string.IsNullOrWhiteSpace(fileName))
                    continue;

                // Filter known false positives (browsers, creative tools, system)
                if (GameConfigStoreFalsePositives.Contains(fileName))
                    continue;

                if (exePath.Contains(@"\Windows\System32", StringComparison.OrdinalIgnoreCase) ||
                    exePath.Contains(@"\Windows\SysWOW64", StringComparison.OrdinalIgnoreCase))
                    continue;

                paths.Add(exePath);
                count++;
            }

            _logger?.LogDebug("GameConfigStore: found {Count} game entries", count);
        }
        catch (Exception ex)
        {
            _logger?.LogWarning(ex, "Failed to scan GameConfigStore");
        }
    }

    /// <summary>
    /// Scans Epic Games Launcher manifest files for installed game directories.
    /// Path: %ProgramData%\Epic\EpicGamesLauncher\Data\Manifests\*.item (JSON)
    /// </summary>
    private void ScanEpicGamesManifests(List<string> directories)
    {
        try
        {
            var manifestDir = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData),
                "Epic", "EpicGamesLauncher", "Data", "Manifests");

            if (!Directory.Exists(manifestDir)) return;

            var count = 0;
            foreach (var file in Directory.EnumerateFiles(manifestDir, "*.item"))
            {
                try
                {
                    using var doc = JsonDocument.Parse(File.ReadAllText(file));
                    if (!doc.RootElement.TryGetProperty("InstallLocation", out var installLoc))
                        continue;

                    var installPath = installLoc.GetString();
                    if (string.IsNullOrWhiteSpace(installPath) || !Directory.Exists(installPath))
                        continue;

                    directories.Add(NormalizeDirectory(installPath));
                    count++;
                }
                catch (Exception ex)
                {
                    _logger?.LogDebug(ex, "Failed to parse Epic manifest: {File}", file);
                }
            }

            _logger?.LogDebug("Epic Games: found {Count} installed games", count);
        }
        catch (Exception ex)
        {
            _logger?.LogWarning(ex, "Failed to scan Epic Games manifests");
        }
    }

    /// <summary>
    /// Scans GOG Galaxy registry for installed game directories.
    /// Path: HKLM\SOFTWARE\WOW6432Node\GOG.com\Games\{GameID}\path
    /// </summary>
    private void ScanGogRegistry(List<string> directories)
    {
        try
        {
            using var key = Registry.LocalMachine.OpenSubKey(@"SOFTWARE\WOW6432Node\GOG.com\Games");
            if (key is null) return;

            var count = 0;
            foreach (var subKeyName in key.GetSubKeyNames())
            {
                using var gameKey = key.OpenSubKey(subKeyName);
                if (gameKey?.GetValue("path") is not string gamePath)
                    continue;

                if (string.IsNullOrWhiteSpace(gamePath) || !Directory.Exists(gamePath))
                    continue;

                directories.Add(NormalizeDirectory(gamePath));
                count++;
            }

            _logger?.LogDebug("GOG: found {Count} installed games", count);
        }
        catch (Exception ex)
        {
            _logger?.LogWarning(ex, "Failed to scan GOG registry");
        }
    }

    /// <summary>
    /// Scans Ubisoft Connect registry for installed game directories.
    /// Path: HKLM\SOFTWARE\WOW6432Node\Ubisoft\Launcher\Installs\{GameID}\InstallDir
    /// </summary>
    private void ScanUbisoftRegistry(List<string> directories)
    {
        try
        {
            using var key = Registry.LocalMachine.OpenSubKey(
                @"SOFTWARE\WOW6432Node\Ubisoft\Launcher\Installs");
            if (key is null) return;

            var count = 0;
            foreach (var subKeyName in key.GetSubKeyNames())
            {
                using var gameKey = key.OpenSubKey(subKeyName);
                if (gameKey?.GetValue("InstallDir") is not string installDir)
                    continue;

                if (string.IsNullOrWhiteSpace(installDir) || !Directory.Exists(installDir))
                    continue;

                directories.Add(NormalizeDirectory(installDir));
                count++;
            }

            _logger?.LogDebug("Ubisoft: found {Count} installed games", count);
        }
        catch (Exception ex)
        {
            _logger?.LogWarning(ex, "Failed to scan Ubisoft registry");
        }
    }

    /// <summary>
    /// Scans EA App registry for installed game directories.
    /// EA uses inconsistent registry structures across games.
    /// Paths: HKLM\SOFTWARE\WOW6432Node\Electronic Arts\* and EA Games\*
    /// </summary>
    private void ScanEaRegistry(List<string> directories)
    {
        try
        {
            string[] eaRegistryPaths =
            [
                @"SOFTWARE\WOW6432Node\Electronic Arts",
                @"SOFTWARE\WOW6432Node\EA Games",
            ];

            var count = 0;
            foreach (var regPath in eaRegistryPaths)
            {
                using var key = Registry.LocalMachine.OpenSubKey(regPath);
                if (key is null) continue;

                foreach (var subKeyName in key.GetSubKeyNames())
                {
                    using var gameKey = key.OpenSubKey(subKeyName);
                    if (gameKey is null) continue;

                    // EA uses different value names across games
                    var installDir = gameKey.GetValue("Install Dir") as string
                                    ?? gameKey.GetValue("InstallDir") as string
                                    ?? gameKey.GetValue("Install Directory") as string;

                    if (string.IsNullOrWhiteSpace(installDir) || !Directory.Exists(installDir))
                        continue;

                    directories.Add(NormalizeDirectory(installDir));
                    count++;
                }
            }

            _logger?.LogDebug("EA: found {Count} installed games", count);
        }
        catch (Exception ex)
        {
            _logger?.LogWarning(ex, "Failed to scan EA registry");
        }
    }

    /// <summary>
    /// Ensures a directory path ends with a directory separator for reliable prefix matching.
    /// </summary>
    private static string NormalizeDirectory(string path)
    {
        return path.EndsWith(Path.DirectorySeparatorChar)
            ? path
            : path + Path.DirectorySeparatorChar;
    }
}
