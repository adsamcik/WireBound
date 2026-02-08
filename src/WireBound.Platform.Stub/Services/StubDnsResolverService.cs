using WireBound.Platform.Abstract.Services;

namespace WireBound.Platform.Stub.Services;

/// <summary>
/// Stub implementation of DNS resolver for unsupported platforms.
/// Returns null for all lookups (no DNS resolution).
/// </summary>
public sealed class StubDnsResolverService : IDnsResolverService
{
    public int CacheSize => 0;
    public int MaxCacheSize { get; set; } = 1000;
    public TimeSpan CacheTtl { get; set; } = TimeSpan.FromMinutes(30);

#pragma warning disable CS0067 // Event is never used
    public event EventHandler<DnsResolvedEventArgs>? HostnameResolved;
#pragma warning restore CS0067

    public Task<string?> ResolveAsync(string ipAddress, CancellationToken cancellationToken = default)
        => Task.FromResult<string?>(null);

    public string? GetCached(string ipAddress) => null;

    public void QueueForResolution(string ipAddress) { }

    public void ClearCache() { }
}
