using System.Diagnostics;
using System.Runtime.Versioning;
using Microsoft.Extensions.Logging;
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
    private const string ServiceFileName = $"{ServiceName}.service";
    private const string PolkitPolicyId = "com.wirebound.elevation";

    public LinuxHelperProcessManager(ILogger<LinuxHelperProcessManager>? logger = null)
    {
        _logger = logger;
    }

    public bool IsRunning => _helperProcess is { HasExited: false } || IsSystemdServiceActive();
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

        if (IsSystemdServiceActive())
        {
            try
            {
                RunCommand("systemctl", $"--user stop {ServiceName}");
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

        // Check executable permission
        var fileInfo = new FileInfo(HelperPath);
        if ((File.GetUnixFileMode(HelperPath) & UnixFileMode.UserExecute) == 0)
            return HelperValidationResult.Invalid("Helper is not executable");

        return HelperValidationResult.Valid();
    }

    /// <summary>
    /// Installs the systemd user service and polkit policy for passwordless startup.
    /// Requires a one-time password prompt via pkexec.
    /// </summary>
    public bool InstallService()
    {
        try
        {
            _logger?.LogInformation("Installing systemd service and polkit policy");

            // Create systemd user service file
            var serviceDir = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
                ".config", "systemd", "user");
            Directory.CreateDirectory(serviceDir);

            var servicePath = Path.Combine(serviceDir, ServiceFileName);
            var serviceContent = $"""
                [Unit]
                Description=WireBound Elevation Helper
                After=network.target

                [Service]
                Type=simple
                ExecStart={HelperPath}
                Restart=on-failure
                RestartSec=5
                Environment=DOTNET_SYSTEM_GLOBALIZATION_INVARIANT=1

                [Install]
                WantedBy=default.target
                """;
            File.WriteAllText(servicePath, serviceContent);

            // Install polkit policy (requires elevation)
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
                      <allow_any>auth_admin_keep</allow_any>
                      <allow_inactive>auth_admin_keep</allow_inactive>
                      <allow_active>auth_admin_keep</allow_active>
                    </defaults>
                    <annotate key="org.freedesktop.policykit.exec.path">{HelperPath}</annotate>
                    <annotate key="org.freedesktop.policykit.exec.allow_gui">false</annotate>
                  </action>
                </policyconfig>
                """;

            var polkitPath = $"/usr/share/polkit-1/actions/{PolkitPolicyId}.policy";
            var tempPolkit = Path.GetTempFileName();
            File.WriteAllText(tempPolkit, polkitContent);

            // Use pkexec to install the polkit policy (one-time prompt)
            var result = RunCommand("pkexec", $"cp {tempPolkit} {polkitPath}");
            File.Delete(tempPolkit);

            if (result != 0)
            {
                _logger?.LogWarning("Failed to install polkit policy (exit code: {Code})", result);
                return false;
            }

            // Reload systemd
            RunCommand("systemctl", "--user daemon-reload");

            _logger?.LogInformation("Service and polkit policy installed successfully");
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
    public bool UninstallService()
    {
        try
        {
            // Stop service if running
            RunCommand("systemctl", $"--user stop {ServiceName}");
            RunCommand("systemctl", $"--user disable {ServiceName}");

            // Remove service file
            var serviceDir = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
                ".config", "systemd", "user");
            var servicePath = Path.Combine(serviceDir, ServiceFileName);
            if (File.Exists(servicePath))
                File.Delete(servicePath);

            // Remove polkit policy
            var polkitPath = $"/usr/share/polkit-1/actions/{PolkitPolicyId}.policy";
            RunCommand("pkexec", $"rm -f {polkitPath}");

            RunCommand("systemctl", "--user daemon-reload");

            _logger?.LogInformation("Service uninstalled successfully");
            return true;
        }
        catch (Exception ex)
        {
            _logger?.LogError(ex, "Failed to uninstall service");
            return false;
        }
    }

    private bool IsServiceInstalled()
    {
        var serviceDir = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
            ".config", "systemd", "user");
        return File.Exists(Path.Combine(serviceDir, ServiceFileName));
    }

    private bool IsSystemdServiceActive()
    {
        try
        {
            return RunCommand("systemctl", $"--user is-active --quiet {ServiceName}") == 0;
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
            var result = RunCommand("systemctl", $"--user start {ServiceName}");
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
                FileName = "pkexec",
                Arguments = helperFullPath,
                UseShellExecute = false,
                CreateNoWindow = true
            };

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

    public async ValueTask DisposeAsync()
    {
        if (_disposed) return;
        _disposed = true;
        await StopAsync();
    }
}
