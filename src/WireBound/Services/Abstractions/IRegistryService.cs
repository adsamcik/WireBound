namespace WireBound.Services.Abstractions;

/// <summary>
/// Abstraction over Windows Registry operations for testability.
/// </summary>
public interface IRegistryService
{
    /// <summary>
    /// Gets a string value from the registry.
    /// </summary>
    /// <param name="keyPath">The registry key path (relative to HKCU).</param>
    /// <param name="valueName">The value name to read.</param>
    /// <returns>The string value, or null if not found.</returns>
    string? GetValue(string keyPath, string valueName);

    /// <summary>
    /// Sets a string value in the registry.
    /// </summary>
    /// <param name="keyPath">The registry key path (relative to HKCU).</param>
    /// <param name="valueName">The value name to set.</param>
    /// <param name="value">The value to write.</param>
    /// <returns>True if successful, false otherwise.</returns>
    bool SetValue(string keyPath, string valueName, string value);

    /// <summary>
    /// Deletes a value from the registry.
    /// </summary>
    /// <param name="keyPath">The registry key path (relative to HKCU).</param>
    /// <param name="valueName">The value name to delete.</param>
    /// <returns>True if successful or value didn't exist, false on error.</returns>
    bool DeleteValue(string keyPath, string valueName);
}

/// <summary>
/// Result of a Windows StartupTask operation.
/// </summary>
public enum StartupTaskState
{
    Disabled,
    Enabled,
    DisabledByUser,
    DisabledByPolicy,
    EnabledByPolicy,
    NotFound,
    Error
}

/// <summary>
/// Abstraction over Windows.ApplicationModel.StartupTask for testability.
/// </summary>
public interface IStartupTaskService
{
    /// <summary>
    /// Gets the current state of the startup task.
    /// </summary>
    /// <param name="taskId">The startup task ID.</param>
    /// <returns>The current state.</returns>
    Task<StartupTaskState> GetStateAsync(string taskId);

    /// <summary>
    /// Requests to enable the startup task.
    /// </summary>
    /// <param name="taskId">The startup task ID.</param>
    /// <returns>The resulting state after the request.</returns>
    Task<StartupTaskState> RequestEnableAsync(string taskId);

    /// <summary>
    /// Disables the startup task.
    /// </summary>
    /// <param name="taskId">The startup task ID.</param>
    /// <returns>True if successful.</returns>
    Task<bool> DisableAsync(string taskId);
}
