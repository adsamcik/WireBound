using WireBound.Platform.Abstract.Services;

namespace WireBound.Platform.Stub.Services;

/// <summary>
/// Stub implementation for platforms that don't support startup registration.
/// </summary>
public sealed class StubStartupService : IStartupService
{
    public bool IsStartupSupported => false;

    public Task<bool> IsStartupEnabledAsync() => Task.FromResult(false);

    public Task<bool> SetStartupEnabledAsync(bool enable) => Task.FromResult(false);

    public Task<StartupResult> SetStartupWithResultAsync(bool enable) =>
        Task.FromResult(StartupResult.Failed(StartupState.NotSupported));

    public Task<StartupState> GetStartupStateAsync() =>
        Task.FromResult(StartupState.NotSupported);

    public Task<bool> EnsureStartupPathUpdatedAsync() =>
        Task.FromResult(true);
}
