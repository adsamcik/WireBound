using WireBound.Platform.Abstract.Services;

namespace WireBound.Platform.Stub.Services;

/// <summary>
/// Stub implementation of network cost provider. Assumes unmetered.
/// </summary>
public sealed class StubNetworkCostProvider : INetworkCostProvider
{
    public Task<bool> IsMeteredAsync() => Task.FromResult(false);
}
