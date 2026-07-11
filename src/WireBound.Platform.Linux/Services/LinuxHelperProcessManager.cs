using System.Diagnostics;
using System.Runtime.Versioning;
using Microsoft.Extensions.Logging;
using Serilog;
using WireBound.Platform.Abstract.Services;

namespace WireBound.Platform.Linux.Services;

/// <summary>
/// Manages the elevated helper process lifecycle on Linux using systemd user service
/// with polkit policy for passwordless startup, with fallback to pkexec.
/// </summary>
[SupportedOSPlatform("linux")]
public sealed class LinuxHelperProcessManager : IHelperProcessManager
{
    private readonly ILogger<LinuxHelperProcessManager>? _logger;
    private Process? _helperProcess;
    private bool _disposed;

    private const string ServiceName = "wirebound-elevation";
    private const string ServiceFileName = "wirebound-elevation@.service";
    private const string PolkitPolicyId = "com.wirebound.elevation";
    private const string SystemdSystemDir = "/etc/systemd/system";

    public LinuxHelperProcessManager(ILogger<LinuxHelperProcessManager>? logger = null)
    {
        _logger = logger;
    }

    public bool IsRunning => _helperProcess is { HasExited: false } || IsSystemdServiceActiveAsync().GetAwaiter().GetResult();
    public int? HelperProcessId => _helperProcess?.HasExited == false ? _helperProcess.Id : null;

    public string HelperPath
    {
        get
        {
            var appDir = AppContext.BaseDirectory;
            return Path.Combine(appDir, "wirebound-elevation");
        }
    }

    public event EventHandler<HelperExitedEventArgs>? HelperExited;

    public async Task<HelperStartResult> StartAsync(CancellationToken cancellationToken = default)
    {
        if (_disposed) return HelperStartResult.Failed("Manager has been disposed");
        if (IsRunning) return HelperStartResult.Success(_helperProcess?.Id ?? 0);

        var validation = ValidateHelper();
        if (!validation.IsValid)
            return HelperStartResult.ValidationFailed(validation.ErrorMessage ?? "Validation failed");

        // Try systemd service first (no password prompt if polkit policy is installed)
        if (IsServiceInstalled())
        {
            _logger?.LogInformation("Starting helper via systemd service");
            var serviceResult = await StartViaSystemdAsync(cancellationToken);
            if (serviceResult.IsSuccess) return serviceResult;

            _logger?.LogWarning("systemd start failed, falling back to pkexec");
        }

        // Fallback: pkexec (graphical password prompt)
        return await StartViaPkexecAsync(cancellationToken);
    }

    public async Task StopAsync(TimeSpan? timeout = null)
    {
        var timeoutMs = (int)(timeout?.TotalMilliseconds ?? 5000);

        if (await IsSystemdServiceActiveAsync())
        {
            try
            {
                await RunCommandAsync("/usr/bin/systemctl", $"stop {ServiceName}@{GetCurrentUid()}");
                _logger?.LogInformation("Stopped systemd service");
            }
            catch (Exception ex)
            {
                _logger?.LogWarning(ex, "Error stopping systemd service");
            }
        }

        if (_helperProcess is { HasExited: false })
        {
            _logger?.LogInformation("Stopping helper process (PID: {Pid})", _helperProcess.Id);
            try
            {
                _helperProcess.Kill();
                await _helperProcess.WaitForExitAsync(new CancellationTokenSource(timeoutMs).Token);
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
    }

    public HelperValidationResult ValidateHelper()
    {
        if (!File.Exists(HelperPath))
            return HelperValidationResult.Invalid($"Helper not found at: {HelperPath}");

        // Reject symlinks — attacker could swap a real binary for a malicious one
        var fileInfo = new FileInfo(HelperPath);
        if (fileInfo.Attributes.HasFlag(FileAttributes.ReparsePoint))
            return HelperValidationResult.Invalid("Helper binary is a symbolic link — refusing to launch");

        // Verify the binary is not world-writable or group-writable
        try
        {
            var mode = File.GetUnixFileMode(HelperPath);
            if (mode.HasFlag(UnixFileMode.OtherWrite))
                return HelperValidationResult.Invalid("Helper binary is world-writable — refusing to launch");
            if (mode.HasFlag(UnixFileMode.GroupWrite))
                return HelperValidationResult.Invalid("Helper binary is group-writable — refusing to launch");
        }
        catch (Exception ex)
        {
            return HelperValidationResult.Invalid($"Cannot verify helper permissions: {ex.Message}");
        }

        // Check executable permission
        if ((File.GetUnixFileMode(HelperPath) & UnixFileMode.UserExecute) == 0)
            return HelperValidationResult.Invalid("Helper is not executable");

        return HelperValidationResult.Valid();
    }

    public bool SupportsPasswordlessElevationSetup => true;

    public Task<bool> IsPasswordlessElevationInstalledAsync() => Task.FromResult(IsServiceInstalled());

    /// <summary>
    /// Installs the systemd system service and polkit policy for passwordless startup.
    /// Requires a one-time password prompt via pkexec.
    /// </summary>
    public async Task<bool> InstallPasswordlessElevationAsync()
    {
        try
        {
            _logger?.LogInformation("Installing systemd system service and polkit policy");

            // System-level service using the @ template so each user gets their own instance.
            // The %i specifier expands to the instance name (UID) at runtime.
            var serviceContent = $"""
                [Unit]
                Description=WireBound Elevation Helper for %i
                After=network.target

                [Service]
                Type=simple
                ExecStart={HelperPath}
                User=root
                Environment=SUDO_UID=%i
                Environment=DOTNET_SYSTEM_GLOBALIZATION_INVARIANT=1

                # Security hardening — restrict the helper to only what it needs
                NoNewPrivileges=yes
                ProtectSystem=strict
                ProtectHome=read-only
                PrivateTmp=yes
                ProtectKernelModules=yes
                ProtectKernelTunables=yes
                ProtectControlGroups=yes
                RestrictSUIDSGID=yes
                RestrictRealtime=yes
                RestrictNamespaces=yes
                MemoryDenyWriteExecute=yes
                LockPersonality=yes

                # Only allow AF_UNIX (for IPC socket) and AF_NETLINK (for network monitoring)
                RestrictAddressFamilies=AF_UNIX AF_NETLINK

                # Read-write access for socket dir and the launching user's secret file
                ReadWritePaths=/run/wirebound /home

                [Install]
                WantedBy=multi-user.target
                """;

            // Write the service file to the system directory using pkexec tee
            // (piping via stdin avoids temp file TOCTOU where another same-user process
            //  could modify the temp file between write and elevated copy)
            var serviceResult = await PipeToElevatedFileAsync(
                $"{SystemdSystemDir}/{ServiceFileName}", serviceContent);
            if (serviceResult != 0)
            {
                _logger?.LogWarning("Failed to install system service file (exit code: {Code})", serviceResult);
                return false;
            }

            // Install polkit policy (requires elevation).
            // Restrict to active local sessions only — remote/inactive sessions cannot trigger elevation.
            var polkitContent = $"""
                <?xml version="1.0" encoding="UTF-8"?>
                <!DOCTYPE policyconfig PUBLIC
                 "-//freedesktop//DTD PolicyKit Policy Configuration 1.0//EN"
                 "https://specifications.freedesktop.org/PolicyKit/1.0/policyconfig.dtd">
                <policyconfig>
                  <action id="{PolkitPolicyId}">
                    <description>WireBound Elevation Helper</description>
                    <message>WireBound needs elevated access for per-process network monitoring</message>
                    <defaults>
                      <allow_any>no</allow_any>
                      <allow_inactive>no</allow_inactive>
                      <allow_active>auth_admin_keep</allow_active>
                    </defaults>
                    <annotate key="org.freedesktop.policykit.exec.path">{HelperPath}</annotate>
                    <annotate key="org.freedesktop.policykit.exec.allow_gui">false</annotate>
                  </action>
                </policyconfig>
                """;

            var polkitPath = $"/usr/share/polkit-1/actions/{PolkitPolicyId}.policy";
            var polkitResult = await PipeToElevatedFileAsync(polkitPath, polkitContent);
            if (polkitResult != 0)
            {
                _logger?.LogWarning("Failed to install polkit policy (exit code: {Code})", polkitResult);
                return false;
            }

            // Reload systemd to pick up the new unit file
            await RunCommandAsync("/usr/bin/systemctl", "daemon-reload");

            _logger?.LogInformation("System service and polkit policy installed successfully");
            return true;
        }
        catch (Exception ex)
        {
            _logger?.LogError(ex, "Failed to install service");
            return false;
        }
    }

    /// <summary>
    /// Uninstalls the systemd service and polkit policy.
    /// </summary>
    public async Task<bool> UninstallPasswordlessElevationAsync()
    {
        try
        {
            var instanceName = $"{ServiceName}@{GetCurrentUid()}";

            // Stop and disable the per-user instance
            await RunCommandAsync("/usr/bin/systemctl", $"stop {instanceName}");
            await RunCommandAsync("/usr/bin/systemctl", $"disable {instanceName}");

            // Remove service template file (requires elevation)
            var servicePath = $"{SystemdSystemDir}/{ServiceFileName}";
            await RunCommandAsync("/usr/bin/pkexec", $"rm -f {servicePath}");

            // Remove polkit policy (requires elevation)
            var polkitPath = $"/usr/share/polkit-1/actions/{PolkitPolicyId}.policy";
            await RunCommandAsync("/usr/bin/pkexec", $"rm -f {polkitPath}");

            await RunCommandAsync("/usr/bin/systemctl", "daemon-reload");

            _logger?.LogInformation("Service uninstalled successfully");
            return true;
        }
        catch (Exception ex)
        {
            _logger?.LogError(ex, "Failed to uninstall service");
            return false;
        }
    }

    private bool IsServiceInstalled() =>
        File.Exists(Path.Combine(SystemdSystemDir, ServiceFileName));

    private async Task<bool> IsSystemdServiceActiveAsync()
    {
        try
        {
            return await RunCommandAsync(
                "/usr/bin/systemctl",
                $"is-active --quiet {ServiceName}@{GetCurrentUid()}") == 0;
        }
        catch
        {
            return false;
        }
    }

    private async Task<HelperStartResult> StartViaSystemdAsync(CancellationToken cancellationToken)
    {
        try
        {
            var instanceName = $"{ServiceName}@{GetCurrentUid()}";
            var result = await RunCommandAsync("/usr/bin/systemctl", $"start {instanceName}");
            if (result != 0)
                return HelperStartResult.Failed($"systemctl start failed (exit code: {result})");

            // Wait for IPC socket to appear
            await WaitForIpcReadyAsync(cancellationToken);
            return HelperStartResult.Success(0);
        }
        catch (Exception ex)
        {
            return HelperStartResult.Failed($"systemd start failed: {ex.Message}");
        }
    }

    private async Task<HelperStartResult> StartViaPkexecAsync(CancellationToken cancellationToken)
    {
        try
        {
            _logger?.LogInformation("Starting helper with pkexec: {Path}", HelperPath);

            // Validate that the helper path resolves within the application directory
            var appDir = Path.GetFullPath(AppContext.BaseDirectory);
            if (!appDir.EndsWith(Path.DirectorySeparatorChar))
                appDir += Path.DirectorySeparatorChar;
            var helperFullPath = Path.GetFullPath(HelperPath);
            if (!helperFullPath.StartsWith(appDir, StringComparison.Ordinal))
            {
                _logger?.LogWarning("Helper path {Path} is outside application directory {AppDir}", helperFullPath, appDir);
                return HelperStartResult.Failed("Helper path is outside application directory");
            }

            var startInfo = new ProcessStartInfo
            {
                FileName = "/usr/bin/pkexec",
                UseShellExecute = false,
                CreateNoWindow = true
            };
            startInfo.ArgumentList.Add(helperFullPath);

            _helperProcess = Process.Start(startInfo);
            if (_helperProcess is null)
                return HelperStartResult.Failed("Failed to start helper process");

            _helperProcess.EnableRaisingEvents = true;
            _helperProcess.Exited += OnHelperProcessExited;

            await WaitForIpcReadyAsync(cancellationToken);

            _logger?.LogInformation("Helper started (PID: {Pid})", _helperProcess.Id);
            return HelperStartResult.Success(_helperProcess.Id);
        }
        catch (Exception ex)
        {
            return HelperStartResult.Failed($"Failed to start helper: {ex.Message}");
        }
    }

    private static async Task WaitForIpcReadyAsync(CancellationToken cancellationToken)
    {
        for (var i = 0; i < 30; i++)
        {
            cancellationToken.ThrowIfCancellationRequested();

            if (File.Exists(WireBound.IPC.IpcConstants.LinuxSocketPath))
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

    /// <summary>
    /// Returns the real UID of the current process by reading /proc/self/status.
    /// Falls back to the UID environment variable if /proc is unavailable.
    /// </summary>
    private static int GetCurrentUid()
    {
        try
        {
            foreach (var line in File.ReadLines("/proc/self/status"))
            {
                if (!line.StartsWith("Uid:", StringComparison.Ordinal))
                    continue;
                var parts = line.Split('\t', StringSplitOptions.RemoveEmptyEntries);
                if (parts.Length >= 2 && int.TryParse(parts[1], out var uid))
                    return uid;
            }
        }
        catch { /* fall through to env var */ }

        if (Environment.GetEnvironmentVariable("UID") is { } uidStr && int.TryParse(uidStr, out var envUid))
            return envUid;

        return -1;
    }

    /// <summary>
    /// Pipes content directly to an elevated file via pkexec tee, avoiding temp file TOCTOU.
    /// </summary>
    private static async Task<int> PipeToElevatedFileAsync(string targetPath, string content)
    {
        var startInfo = new ProcessStartInfo
        {
            FileName = "/usr/bin/pkexec",
            UseShellExecute = false,
            CreateNoWindow = true,
            RedirectStandardInput = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true
        };
        startInfo.ArgumentList.Add("tee");
        startInfo.ArgumentList.Add(targetPath);

        using var process = Process.Start(startInfo)!;

        await process.StandardInput.WriteAsync(content);
        process.StandardInput.Close();

        // Drain stdout (tee echoes input) and stderr
        var stdoutTask = process.StandardOutput.ReadToEndAsync();
        var stderrTask = process.StandardError.ReadToEndAsync();

        process.WaitForExit(10000);

        if (!process.HasExited)
        {
            process.Kill();
            Log.Warning("pkexec tee timed out for: {Path}", targetPath);
            return -1;
        }

        await stderrTask.ConfigureAwait(false);
        return process.ExitCode;
    }

    private static async Task<int> RunCommandAsync(string fileName, string arguments)
    {
        var startInfo = new ProcessStartInfo
        {
            FileName = fileName,
            UseShellExecute = false,
            CreateNoWindow = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true
        };
        // Use ArgumentList for safe argument handling (no shell interpretation)
        foreach (var arg in arguments.Split(' ', StringSplitOptions.RemoveEmptyEntries))
            startInfo.ArgumentList.Add(arg);

        using var process = Process.Start(startInfo)!;

        // Drain stdout/stderr asynchronously to prevent buffer-full deadlock
        var stdoutTask = process.StandardOutput.ReadToEndAsync();
        var stderrTask = process.StandardError.ReadToEndAsync();

        process.WaitForExit(10000);

        if (!process.HasExited)
        {
            process.Kill();
            Log.Warning("Command timed out: {FileName}", fileName);
            return -1;
        }

        var stderr = await stderrTask.ConfigureAwait(false);
        if (process.ExitCode != 0 && !string.IsNullOrWhiteSpace(stderr))
            Log.Debug("Command {FileName} failed with stderr: {Stderr}", fileName, stderr);

        return process.ExitCode;
    }

    public async ValueTask DisposeAsync()
    {
        if (_disposed) return;
        _disposed = true;
        await StopAsync();
    }
}
