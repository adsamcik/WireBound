using Microsoft.Extensions.Logging;

namespace WireBound.Services.Abstractions;

/// <summary>
/// Windows.ApplicationModel.StartupTask implementation.
/// </summary>
public sealed class WindowsStartupTaskService : IStartupTaskService
{
    private readonly ILogger<WindowsStartupTaskService> _logger;

    public WindowsStartupTaskService(ILogger<WindowsStartupTaskService> logger)
    {
        _logger = logger;
    }

    public async Task<StartupTaskState> GetStateAsync(string taskId)
    {
        try
        {
            var startupTask = await Windows.ApplicationModel.StartupTask
                .GetAsync(taskId)
                .AsTask()
                .ConfigureAwait(false);

            return MapState(startupTask.State);
        }
        catch (ArgumentException)
        {
            _logger.LogWarning("StartupTask '{TaskId}' not found in manifest", taskId);
            return StartupTaskState.NotFound;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error getting StartupTask state for '{TaskId}'", taskId);
            return StartupTaskState.Error;
        }
    }

    public async Task<StartupTaskState> RequestEnableAsync(string taskId)
    {
        try
        {
            var startupTask = await Windows.ApplicationModel.StartupTask
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
            return StartupTaskState.NotFound;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error enabling StartupTask '{TaskId}'", taskId);
            return StartupTaskState.Error;
        }
    }

    public async Task<bool> DisableAsync(string taskId)
    {
        try
        {
            var startupTask = await Windows.ApplicationModel.StartupTask
                .GetAsync(taskId)
                .AsTask()
                .ConfigureAwait(false);

            startupTask.Disable();

            // Verify the disable took effect
            var verifyTask = await Windows.ApplicationModel.StartupTask
                .GetAsync(taskId)
                .AsTask()
                .ConfigureAwait(false);

            var newState = verifyTask.State;
            var success = newState == Windows.ApplicationModel.StartupTaskState.Disabled ||
                          newState == Windows.ApplicationModel.StartupTaskState.DisabledByUser ||
                          newState == Windows.ApplicationModel.StartupTaskState.DisabledByPolicy;

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

    private static StartupTaskState MapState(Windows.ApplicationModel.StartupTaskState state)
    {
        return state switch
        {
            Windows.ApplicationModel.StartupTaskState.Disabled => StartupTaskState.Disabled,
            Windows.ApplicationModel.StartupTaskState.Enabled => StartupTaskState.Enabled,
            Windows.ApplicationModel.StartupTaskState.DisabledByUser => StartupTaskState.DisabledByUser,
            Windows.ApplicationModel.StartupTaskState.DisabledByPolicy => StartupTaskState.DisabledByPolicy,
            Windows.ApplicationModel.StartupTaskState.EnabledByPolicy => StartupTaskState.EnabledByPolicy,
            _ => StartupTaskState.Error
        };
    }
}
