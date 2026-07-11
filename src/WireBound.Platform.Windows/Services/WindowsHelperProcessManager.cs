using System.Diagnostics;
using System.Runtime.Versioning;
using System.Security.AccessControl;
using System.Security.Principal;
using Microsoft.Extensions.Logging;
using WireBound.Platform.Abstract.Services;

namespace WireBound.Platform.Windows.Services;

/// <summary>
/// Manages the elevated helper process lifecycle on Windows using Task Scheduler
/// for UAC-free startup, with fallback to direct process launch with UAC prompt.
/// </summary>
[SupportedOSPlatform("windows")]
public sealed class WindowsHelperProcessManager : IHelperProcessManager
{
    private readonly ILogger<WindowsHelperProcessManager>? _logger;
    private Process? _helperProcess;
    private bool _disposed;

    private const string TaskName = "WireBound Elevation Helper";
    private const string TaskFolder = "\\WireBound";

    public WindowsHelperProcessManager(ILogger<WindowsHelperProcessManager>? logger = null)
    {
        _logger = logger;
    }

    public bool IsRunning => _helperProcess is { HasExited: false };
    public int? HelperProcessId => IsRunning ? _helperProcess?.Id : null;

    public string HelperPath
    {
        get
        {
            var appDir = AppContext.BaseDirectory;
            return Path.Combine(appDir, "WireBound.Elevation.exe");
        }
    }

    /// <summary>
    /// Gets the SID of the current (non-elevated) user to pass to the helper process.
    /// </summary>
    private static string CurrentUserSid =>
        WindowsIdentity.GetCurrent().User!.Value;

    public event EventHandler<HelperExitedEventArgs>? HelperExited;

    public async Task<HelperStartResult> StartAsync(CancellationToken cancellationToken = default)
    {
        if (_disposed) return HelperStartResult.Failed("Manager has been disposed");
        if (IsRunning) return HelperStartResult.Success(_helperProcess!.Id);

        var validation = ValidateHelper();
        if (!validation.IsValid)
            return HelperStartResult.ValidationFailed(validation.ErrorMessage ?? "Validation failed");

        // Try Task Scheduler first (no UAC prompt)
        if (await IsScheduledTaskRegistered())
        {
            _logger?.LogInformation("Starting helper via Task Scheduler (no UAC prompt)");
            var taskResult = await StartViaTaskSchedulerAsync(cancellationToken);
            if (taskResult.IsSuccess) return taskResult;

            _logger?.LogWarning("Task Scheduler start failed, falling back to direct launch");
        }

        // Fallback: direct launch with UAC
        return await StartDirectAsync(cancellationToken);
    }

    public async Task StopAsync(TimeSpan? timeout = null)
    {
        if (_helperProcess is null || _helperProcess.HasExited)
        {
            _helperProcess = null;
            return;
        }

        var timeoutMs = (int)(timeout?.TotalMilliseconds ?? 5000);
        _logger?.LogInformation("Stopping helper process (PID: {Pid})", _helperProcess.Id);

        try
        {
            // Give the process time to exit gracefully
            if (!_helperProcess.HasExited)
            {
                _helperProcess.Kill();
                using var stopCts = new CancellationTokenSource(timeoutMs);
                await _helperProcess.WaitForExitAsync(stopCts.Token);
            }
        }
        catch (Exception ex)
        {
            _logger?.LogWarning(ex, "Error stopping helper process");
        }
        finally
        {
            _helperProcess?.Dispose();
            _helperProcess = null;
        }
    }

    public HelperValidationResult ValidateHelper()
    {
        if (!File.Exists(HelperPath))
            return HelperValidationResult.Invalid($"Helper not found at: {HelperPath}");

        // Reject reparse points (symlinks, junctions) — attacker could redirect to arbitrary binary
        var fileInfo = new FileInfo(HelperPath);
        if (fileInfo.Attributes.HasFlag(FileAttributes.ReparsePoint))
            return HelperValidationResult.Invalid("Helper binary is a reparse point — refusing to launch");

        // Verify the binary path is canonical (no .. traversal)
        var canonicalPath = Path.GetFullPath(HelperPath);
        if (!string.Equals(canonicalPath, HelperPath, StringComparison.OrdinalIgnoreCase))
            return HelperValidationResult.Invalid(
                $"Helper path is not canonical. Expected: {canonicalPath}, Got: {HelperPath}");

        // Block launch if the helper directory grants write access to Everyone/Users.
        // A writable install directory allows attackers to replace the helper binary.
        var helperDir = Path.GetDirectoryName(HelperPath)!;
        try
        {
            var dirInfo = new DirectoryInfo(helperDir);
            var acl = dirInfo.GetAccessControl();
            var rules = acl.GetAccessRules(true, true, typeof(SecurityIdentifier));

            foreach (FileSystemAccessRule rule in rules)
            {
                var sid = (SecurityIdentifier)rule.IdentityReference;
                if ((sid.IsWellKnown(WellKnownSidType.WorldSid) ||
                     sid.IsWellKnown(WellKnownSidType.BuiltinUsersSid)) &&
                    rule.AccessControlType == AccessControlType.Allow &&
                    (rule.FileSystemRights & FileSystemRights.Write) != 0)
                {
                    return HelperValidationResult.Invalid(
                        $"Helper directory {helperDir} has permissive write access for {sid.Value}. " +
                        "Install to a protected location like Program Files.");
                }
            }
        }
        catch (Exception ex)
        {
            _logger?.LogDebug("Could not verify helper directory ACL: {Error}", ex.Message);
        }

        return HelperValidationResult.Valid();
    }

    /// <summary>
    /// Registers a scheduled task to run the helper at logon with highest privileges.
    /// Requires a one-time UAC prompt.
    /// </summary>
    public async Task<bool> RegisterScheduledTask()
    {
        try
        {
            _logger?.LogInformation("Registering scheduled task for helper auto-start");

            var sid = CurrentUserSid;

            // Use schtasks.exe to create a task that runs at logon with highest privileges
            var args = $"/Create /TN \"{TaskFolder}\\{TaskName}\" " +
                       $"/TR \"\\\"{HelperPath}\\\" --caller-sid {sid}\" " +
                       "/SC ONLOGON " +
                       "/RL HIGHEST " +
                       "/F " + // Force overwrite if exists
                       "/NP"; // No password prompt

            var result = await RunSchtasksAsync(args);
            if (result == 0)
            {
                _logger?.LogInformation("Scheduled task registered successfully");
                return true;
            }

            _logger?.LogError("Failed to register scheduled task (exit code: {Code})", result);
            return false;
        }
        catch (Exception ex)
        {
            _logger?.LogError(ex, "Failed to register scheduled task");
            return false;
        }
    }

    /// <summary>
    /// Unregisters the scheduled task.
    /// </summary>
    public async Task<bool> UnregisterScheduledTask()
    {
        try
        {
            var result = await RunSchtasksAsync($"/Delete /TN \"{TaskFolder}\\{TaskName}\" /F");
            return result == 0;
        }
        catch (Exception ex)
        {
            _logger?.LogError(ex, "Failed to unregister scheduled task");
            return false;
        }
    }

    public async Task<bool> IsScheduledTaskRegistered()
    {
        try
        {
            var result = await RunSchtasksAsync($"/Query /TN \"{TaskFolder}\\{TaskName}\"");
            return result == 0;
        }
        catch
        {
            return false;
        }
    }

    // Windows already achieves passwordless on-demand starts via the "Start Helper at Login"
    // scheduled task (see WindowsStartupService.SetHelperStartupEnabledAsync), which is exposed
    // through IStartupService and bound to the existing Settings toggle. Exposing a second,
    // redundant control here would duplicate that UI, so this always reports unsupported.
    public bool SupportsPasswordlessElevationSetup => false;

    public Task<bool> IsPasswordlessElevationInstalledAsync() => Task.FromResult(false);

    public Task<bool> InstallPasswordlessElevationAsync() => Task.FromResult(false);

    public Task<bool> UninstallPasswordlessElevationAsync() => Task.FromResult(false);

    private async Task<HelperStartResult> StartViaTaskSchedulerAsync(CancellationToken cancellationToken)
    {
        try
        {
            var result = await RunSchtasksAsync($"/Run /TN \"{TaskFolder}\\{TaskName}\"");
            if (result != 0)
                return HelperStartResult.Failed("Task Scheduler run failed");

            // Wait for the helper process to appear
            return await WaitForHelperProcessAsync(cancellationToken);
        }
        catch (Exception ex)
        {
            return HelperStartResult.Failed($"Task Scheduler start failed: {ex.Message}");
        }
    }

    private async Task<HelperStartResult> StartDirectAsync(CancellationToken cancellationToken)
    {
        try
        {
            _logger?.LogInformation("Starting helper with UAC elevation: {Path}", HelperPath);

            var startInfo = new ProcessStartInfo
            {
                FileName = HelperPath,
                Arguments = $"--caller-sid {CurrentUserSid}",
                UseShellExecute = true,
                Verb = "runas", // Triggers UAC prompt
                CreateNoWindow = true
            };

            _helperProcess = Process.Start(startInfo);
            if (_helperProcess is null)
                return HelperStartResult.Failed("Failed to start helper process");

            _helperProcess.EnableRaisingEvents = true;
            _helperProcess.Exited += OnHelperProcessExited;

            // Wait for the IPC endpoint to become available
            await WaitForIpcReadyAsync(cancellationToken);

            _logger?.LogInformation("Helper started (PID: {Pid})", _helperProcess.Id);
            return HelperStartResult.Success(_helperProcess.Id);
        }
        catch (System.ComponentModel.Win32Exception ex) when (ex.NativeErrorCode == 1223)
        {
            // ERROR_CANCELLED - user cancelled UAC prompt
            return HelperStartResult.Cancelled();
        }
        catch (Exception ex)
        {
            return HelperStartResult.Failed($"Failed to start helper: {ex.Message}");
        }
    }

    private async Task<HelperStartResult> WaitForHelperProcessAsync(CancellationToken cancellationToken)
    {
        var helperName = Path.GetFileNameWithoutExtension(HelperPath);

        for (var i = 0; i < 20; i++) // 2 second timeout
        {
            cancellationToken.ThrowIfCancellationRequested();

            var processes = Process.GetProcessesByName(helperName);
            try
            {
                if (processes.Length > 0)
                {
                    _helperProcess = processes[0];
                    _helperProcess.EnableRaisingEvents = true;
                    _helperProcess.Exited += OnHelperProcessExited;

                    // Dispose all other Process objects to avoid handle leaks
                    for (var j = 1; j < processes.Length; j++)
                        processes[j].Dispose();

                    await WaitForIpcReadyAsync(cancellationToken);
                    return HelperStartResult.Success(_helperProcess.Id);
                }
            }
            catch
            {
                // Dispose all on failure
                foreach (var p in processes)
                    p.Dispose();
                throw;
            }

            await Task.Delay(100, cancellationToken);
        }

        return HelperStartResult.Failed("Helper process did not start within timeout");
    }

    private static async Task WaitForIpcReadyAsync(CancellationToken cancellationToken)
    {
        // Wait for the named pipe to become available
        for (var i = 0; i < 30; i++) // 3 second timeout
        {
            cancellationToken.ThrowIfCancellationRequested();

            if (File.Exists(@$"\\.\pipe\{WireBound.IPC.IpcConstants.WindowsPipeName}"))
                return;

            await Task.Delay(100, cancellationToken);
        }
    }

    private void OnHelperProcessExited(object? sender, EventArgs e)
    {
        var exitCode = _helperProcess?.ExitCode ?? -1;
        _logger?.LogWarning("Helper process exited (code: {Code})", exitCode);
        HelperExited?.Invoke(this, new HelperExitedEventArgs(exitCode, wasExpected: false));
    }

    private async Task<int> RunSchtasksAsync(string arguments)
    {
        var startInfo = new ProcessStartInfo
        {
            FileName = Path.Combine(Environment.SystemDirectory, "schtasks.exe"),
            Arguments = arguments,
            UseShellExecute = false,
            CreateNoWindow = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true
        };

        using var process = Process.Start(startInfo)!;
        // Drain stdout/stderr asynchronously to prevent buffer-full deadlock
        var stdoutTask = process.StandardOutput.ReadToEndAsync();
        var stderrTask = process.StandardError.ReadToEndAsync();
        process.WaitForExit(10000);

        if (!process.HasExited)
        {
            process.Kill();
            _logger?.LogWarning("schtasks timed out");
            return -1;
        }

        var stderr = await stderrTask;
        if (process.ExitCode != 0 && !string.IsNullOrWhiteSpace(stderr))
            _logger?.LogDebug("schtasks failed with stderr: {Stderr}", stderr);

        return process.ExitCode;
    }

    public async ValueTask DisposeAsync()
    {
        if (_disposed) return;
        _disposed = true;
        await StopAsync();
    }
}
