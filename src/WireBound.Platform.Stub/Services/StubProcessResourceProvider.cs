using WireBound.Platform.Abstract.Models;
using WireBound.Platform.Abstract.Services;

namespace WireBound.Platform.Stub.Services;

/// <summary>
/// Stub implementation of per-process resource data provider.
/// Returns an empty list for unsupported/test platforms.
/// </summary>
public sealed class StubProcessResourceProvider : IProcessResourceProvider
{
    public Task<IReadOnlyList<ProcessResourceData>> GetProcessResourceDataAsync(
        CancellationToken cancellationToken = default)
    {
        return Task.FromResult<IReadOnlyList<ProcessResourceData>>([]);
    }
}
