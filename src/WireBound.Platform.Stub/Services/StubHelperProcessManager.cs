using WireBound.Platform.Abstract.Services;

namespace WireBound.Platform.Stub.Services;

/// <summary>
/// Stub implementation of <see cref="IHelperProcessManager"/> for unsupported platforms.
/// </summary>
public sealed class StubHelperProcessManager : IHelperProcessManager
{
    public bool IsRunning => false;
    public int? HelperProcessId => null;
    public string HelperPath => string.Empty;

#pragma warning disable CS0067 // Event is never used (stub implementation)
    public event EventHandler<HelperExitedEventArgs>? HelperExited;
#pragma warning restore CS0067

    public Task<HelperStartResult> StartAsync(CancellationToken cancellationToken = default) =>
        Task.FromResult(HelperStartResult.Failed("Not supported on this platform"));

    public Task StopAsync(TimeSpan? timeout = null) =>
        Task.CompletedTask;

    public HelperValidationResult ValidateHelper() =>
        HelperValidationResult.Invalid("Not supported on this platform");

    public ValueTask DisposeAsync() => ValueTask.CompletedTask;
}
