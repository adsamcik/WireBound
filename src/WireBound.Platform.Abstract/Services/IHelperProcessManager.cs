namespace WireBound.Platform.Abstract.Services;

/// <summary>
/// Manages the lifecycle of the minimal elevated helper process.
/// </summary>
/// <remarks>
/// <para>
/// <b>Security Design Principles:</b>
/// </para>
/// <para>
/// The helper process follows these security principles:
/// <list type="bullet">
/// <item><b>Minimal Attack Surface:</b> Helper only exposes data collection APIs,
/// no file system access, no network access, no UI</item>
/// <item><b>Process Isolation:</b> Runs as a separate process, crashes don't affect main app</item>
/// <item><b>Client Validation:</b> Validates connecting process before accepting commands</item>
/// <item><b>Session Management:</b> Time-limited sessions with automatic expiry</item>
/// <item><b>Rate Limiting:</b> Prevents abuse through request rate limiting</item>
/// <item><b>Audit Logging:</b> All operations are logged for security auditing</item>
/// </list>
/// </para>
/// <para>
/// <b>Helper Capabilities (by design, kept minimal):</b>
/// <list type="bullet">
/// <item>Windows: ETW session for network byte counters per connection</item>
/// <item>Linux: eBPF program for network byte counters per connection</item>
/// <item>Both: Read-only access to connection statistics</item>
/// </list>
/// </para>
/// <para>
/// <b>Helper Explicitly CANNOT:</b>
/// <list type="bullet">
/// <item>Write to the file system</item>
/// <item>Make network connections</item>
/// <item>Start other processes</item>
/// <item>Modify system configuration</item>
/// <item>Access user data or credentials</item>
/// </list>
/// </para>
/// </remarks>
public interface IHelperProcessManager : IAsyncDisposable
{
    /// <summary>
    /// Whether the helper process is currently running.
    /// </summary>
    bool IsRunning { get; }

    /// <summary>
    /// Process ID of the helper, if running.
    /// </summary>
    int? HelperProcessId { get; }

    /// <summary>
    /// The path to the helper executable.
    /// </summary>
    string HelperPath { get; }

    /// <summary>
    /// Starts the elevated helper process.
    /// </summary>
    /// <remarks>
    /// <para>
    /// This method:
    /// <list type="number">
    /// <item>Validates the helper executable exists and has valid signature</item>
    /// <item>Launches the helper with elevation (UAC on Windows, pkexec on Linux)</item>
    /// <item>Waits for the helper to initialize and open its IPC endpoint</item>
    /// <item>Returns success only when helper is ready to accept connections</item>
    /// </list>
    /// </para>
    /// </remarks>
    /// <param name="cancellationToken">Cancellation token</param>
    /// <returns>Result indicating success or failure with details</returns>
    Task<HelperStartResult> StartAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Gracefully stops the helper process.
    /// </summary>
    /// <remarks>
    /// Sends a shutdown command to the helper and waits for clean exit.
    /// If the helper doesn't exit within timeout, it will be forcefully terminated.
    /// </remarks>
    /// <param name="timeout">Maximum time to wait for graceful shutdown</param>
    Task StopAsync(TimeSpan? timeout = null);

    /// <summary>
    /// Validates that the helper executable is legitimate and unmodified.
    /// </summary>
    /// <remarks>
    /// Performs security checks on the helper executable:
    /// <list type="bullet">
    /// <item>File exists at expected location</item>
    /// <item>Digital signature is valid (Windows)</item>
    /// <item>Hash matches expected value</item>
    /// <item>File permissions are correct (Linux)</item>
    /// </list>
    /// </remarks>
    /// <returns>Validation result with details</returns>
    HelperValidationResult ValidateHelper();

    /// <summary>
    /// Event raised when the helper process exits unexpectedly.
    /// </summary>
    event EventHandler<HelperExitedEventArgs>? HelperExited;
}

/// <summary>
/// Result of starting the helper process.
/// </summary>
public readonly record struct HelperStartResult(
    HelperStartStatus Status,
    string? ErrorMessage = null,
    int? ProcessId = null)
{
    public static HelperStartResult Success(int processId) =>
        new(HelperStartStatus.Success, ProcessId: processId);

    public static HelperStartResult Failed(string message) =>
        new(HelperStartStatus.Failed, message);

    public static HelperStartResult Cancelled() =>
        new(HelperStartStatus.UserCancelled);

    public static HelperStartResult ValidationFailed(string message) =>
        new(HelperStartStatus.ValidationFailed, message);

    public static HelperStartResult NotFound() =>
        new(HelperStartStatus.HelperNotFound, "Helper executable not found");

    public bool IsSuccess => Status == HelperStartStatus.Success;
}

/// <summary>
/// Status of helper start attempt.
/// </summary>
public enum HelperStartStatus
{
    /// <summary>Helper started successfully.</summary>
    Success,
    /// <summary>User cancelled the elevation prompt.</summary>
    UserCancelled,
    /// <summary>Helper executable was not found.</summary>
    HelperNotFound,
    /// <summary>Helper failed security validation.</summary>
    ValidationFailed,
    /// <summary>Failed to start helper process.</summary>
    Failed
}

/// <summary>
/// Result of helper executable validation.
/// </summary>
public readonly record struct HelperValidationResult(
    bool IsValid,
    string? ErrorMessage = null)
{
    public static HelperValidationResult Valid() => new(true);
    public static HelperValidationResult Invalid(string message) => new(false, message);
}

/// <summary>
/// Event args for helper process exit.
/// </summary>
public class HelperExitedEventArgs : EventArgs
{
    /// <summary>Exit code of the helper process.</summary>
    public int ExitCode { get; }

    /// <summary>Whether the exit was expected (graceful shutdown).</summary>
    public bool WasExpected { get; }

    /// <summary>Reason for exit, if known.</summary>
    public string? Reason { get; }

    public HelperExitedEventArgs(int exitCode, bool wasExpected, string? reason = null)
    {
        ExitCode = exitCode;
        WasExpected = wasExpected;
        Reason = reason;
    }
}
