namespace WireBound.Platform.Abstract.Services;

/// <summary>
/// Service for managing application startup registration with the operating system.
/// </summary>
public interface IStartupService
{
    /// <summary>
    /// Gets whether the application is currently registered to start with the OS.
    /// </summary>
    Task<bool> IsStartupEnabledAsync();

    /// <summary>
    /// Gets whether startup registration is available on the current platform.
    /// </summary>
    bool IsStartupSupported { get; }

    /// <summary>
    /// Enables or disables startup with the operating system.
    /// </summary>
    /// <param name="enable">True to enable startup, false to disable.</param>
    /// <returns>True if the operation succeeded, false otherwise.</returns>
    Task<bool> SetStartupEnabledAsync(bool enable);

    /// <summary>
    /// Enables or disables startup and returns the resulting state.
    /// This is more efficient than calling SetStartupEnabledAsync followed by GetStartupStateAsync.
    /// </summary>
    /// <param name="enable">True to enable startup, false to disable.</param>
    /// <returns>A result containing success status and the resulting startup state.</returns>
    Task<StartupResult> SetStartupWithResultAsync(bool enable);

    /// <summary>
    /// Gets the current startup state with detailed status information.
    /// </summary>
    Task<StartupState> GetStartupStateAsync();
}

/// <summary>
/// Result of a startup enable/disable operation.
/// </summary>
public readonly struct StartupResult
{
    /// <summary>
    /// Whether the operation completed successfully.
    /// </summary>
    public bool Success { get; init; }

    /// <summary>
    /// The resulting startup state after the operation.
    /// </summary>
    public StartupState State { get; init; }

    /// <summary>
    /// Creates a successful result.
    /// </summary>
    public static StartupResult Succeeded(StartupState state) => new() { Success = true, State = state };

    /// <summary>
    /// Creates a failed result.
    /// </summary>
    public static StartupResult Failed(StartupState state) => new() { Success = false, State = state };
}

/// <summary>
/// Represents the current startup registration state.
/// </summary>
public enum StartupState
{
    /// <summary>
    /// Startup is disabled.
    /// </summary>
    Disabled,

    /// <summary>
    /// Startup is enabled and will run on login.
    /// </summary>
    Enabled,

    /// <summary>
    /// Startup was disabled by the user in system settings.
    /// </summary>
    DisabledByUser,

    /// <summary>
    /// Startup was disabled by system policy.
    /// </summary>
    DisabledByPolicy,

    /// <summary>
    /// Startup registration is not supported on this platform.
    /// </summary>
    NotSupported,

    /// <summary>
    /// An error occurred while checking startup state.
    /// </summary>
    Error
}
