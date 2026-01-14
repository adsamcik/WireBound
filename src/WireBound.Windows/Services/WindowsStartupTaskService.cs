using Microsoft.Extensions.Logging;

namespace WireBound.Windows.Services;

/// <summary>
/// Windows.ApplicationModel.StartupTask implementation.
/// </summary>
public sealed class WindowsStartupTaskService : IWindowsStartupTaskService
{
    private readonly ILogger<WindowsStartupTaskService> _logger;

    public WindowsStartupTaskService(ILogger<WindowsStartupTaskService> logger)
    {
        _logger = logger;
    }

    public async Task<WindowsStartupTaskState> GetStateAsync(string taskId)
    {
        try
        {
            var startupTask = await global::Windows.ApplicationModel.StartupTask
                .GetAsync(taskId)
                .AsTask()
                .ConfigureAwait(false);

            return MapState(startupTask.State);
        }
        catch (ArgumentException)
        {
            _logger.LogWarning("StartupTask '{TaskId}' not found in manifest", taskId);
            return WindowsStartupTaskState.NotFound;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error getting StartupTask state for '{TaskId}'", taskId);
            return WindowsStartupTaskState.Error;
        }
    }

    public async Task<WindowsStartupTaskState> RequestEnableAsync(string taskId)
    {
        try
        {
            var startupTask = await global::Windows.ApplicationModel.StartupTask
                .GetAsync(taskId)
                .AsTask()
                .ConfigureAwait(false);

            var state = await startupTask.RequestEnableAsync().AsTask().ConfigureAwait(false);
            _logger.LogInformation("StartupTask '{TaskId}' enable request returned: {State}", taskId, state);
            return MapState(state);
        }
        catch (ArgumentException)
        {
            _logger.LogWarning("StartupTask '{TaskId}' not found in manifest", taskId);
            return WindowsStartupTaskState.NotFound;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error enabling StartupTask '{TaskId}'", taskId);
            return WindowsStartupTaskState.Error;
        }
    }

    public async Task<bool> DisableAsync(string taskId)
    {
        try
        {
            var startupTask = await global::Windows.ApplicationModel.StartupTask
                .GetAsync(taskId)
                .AsTask()
                .ConfigureAwait(false);

            startupTask.Disable();

            var verifyTask = await global::Windows.ApplicationModel.StartupTask
                .GetAsync(taskId)
                .AsTask()
                .ConfigureAwait(false);

            var newState = verifyTask.State;
            var success = newState == global::Windows.ApplicationModel.StartupTaskState.Disabled ||
                          newState == global::Windows.ApplicationModel.StartupTaskState.DisabledByUser ||
                          newState == global::Windows.ApplicationModel.StartupTaskState.DisabledByPolicy;

            if (!success)
            {
                _logger.LogWarning("StartupTask '{TaskId}' disable may not have taken effect. State: {State}", taskId, newState);
            }

            return success;
        }
        catch (ArgumentException)
        {
            _logger.LogWarning("StartupTask '{TaskId}' not found in manifest", taskId);
            return false;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error disabling StartupTask '{TaskId}'", taskId);
            return false;
        }
    }

    private static WindowsStartupTaskState MapState(global::Windows.ApplicationModel.StartupTaskState state)
    {
        return state switch
        {
            global::Windows.ApplicationModel.StartupTaskState.Disabled => WindowsStartupTaskState.Disabled,
            global::Windows.ApplicationModel.StartupTaskState.DisabledByUser => WindowsStartupTaskState.DisabledByUser,
            global::Windows.ApplicationModel.StartupTaskState.DisabledByPolicy => WindowsStartupTaskState.DisabledByPolicy,
            global::Windows.ApplicationModel.StartupTaskState.Enabled => WindowsStartupTaskState.Enabled,
            global::Windows.ApplicationModel.StartupTaskState.EnabledByPolicy => WindowsStartupTaskState.EnabledByPolicy,
            _ => WindowsStartupTaskState.Error
        };
    }
}
