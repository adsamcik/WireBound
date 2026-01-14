namespace WireBound.Windows.Services;

/// <summary>
/// Abstraction over Windows Registry operations for testability.
/// </summary>
public interface IWindowsRegistryService
{
    /// <summary>
    /// Gets a string value from the registry.
    /// </summary>
    string? GetValue(string keyPath, string valueName);

    /// <summary>
    /// Sets a string value in the registry.
    /// </summary>
    bool SetValue(string keyPath, string valueName, string value);

    /// <summary>
    /// Deletes a value from the registry.
    /// </summary>
    bool DeleteValue(string keyPath, string valueName);
}

/// <summary>
/// Result of a Windows StartupTask operation.
/// </summary>
public enum WindowsStartupTaskState
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
public interface IWindowsStartupTaskService
{
    /// <summary>
    /// Gets the current state of the startup task.
    /// </summary>
    Task<WindowsStartupTaskState> GetStateAsync(string taskId);

    /// <summary>
    /// Requests to enable the startup task.
    /// </summary>
    Task<WindowsStartupTaskState> RequestEnableAsync(string taskId);

    /// <summary>
    /// Disables the startup task.
    /// </summary>
    Task<bool> DisableAsync(string taskId);
}
