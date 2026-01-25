namespace WireBound.Platform.Abstract.Services;

/// <summary>
/// Service for resolving IP addresses to hostnames with caching.
/// Performs async DNS lookups and maintains an LRU cache.
/// </summary>
public interface IDnsResolverService
{
    /// <summary>
    /// Resolve an IP address to a hostname asynchronously.
    /// Results are cached for future lookups.
    /// </summary>
    /// <param name="ipAddress">IP address to resolve (IPv4 or IPv6)</param>
    /// <param name="cancellationToken">Cancellation token</param>
    /// <returns>Hostname if resolved, null if resolution failed or IP is private/local</returns>
    Task<string?> ResolveAsync(string ipAddress, CancellationToken cancellationToken = default);

    /// <summary>
    /// Get a cached hostname if available (non-blocking).
    /// Returns null if not in cache - call ResolveAsync to populate.
    /// </summary>
    /// <param name="ipAddress">IP address to look up</param>
    /// <returns>Cached hostname or null</returns>
    string? GetCached(string ipAddress);

    /// <summary>
    /// Queue an IP address for background resolution.
    /// Does not block - resolution happens asynchronously.
    /// </summary>
    /// <param name="ipAddress">IP address to resolve</param>
    void QueueForResolution(string ipAddress);

    /// <summary>
    /// Clear the entire DNS cache
    /// </summary>
    void ClearCache();

    /// <summary>
    /// Number of entries currently in the cache
    /// </summary>
    int CacheSize { get; }

    /// <summary>
    /// Maximum cache size (oldest entries evicted when exceeded)
    /// </summary>
    int MaxCacheSize { get; set; }

    /// <summary>
    /// Cache entry TTL (entries older than this are considered stale)
    /// </summary>
    TimeSpan CacheTtl { get; set; }

    /// <summary>
    /// Raised when a hostname is resolved (for cache-then-update patterns)
    /// </summary>
    event EventHandler<DnsResolvedEventArgs>? HostnameResolved;
}

/// <summary>
/// Event args for hostname resolution events
/// </summary>
public class DnsResolvedEventArgs : EventArgs
{
    public string IpAddress { get; }
    public string? Hostname { get; }
    public bool WasCached { get; }

    public DnsResolvedEventArgs(string ipAddress, string? hostname, bool wasCached = false)
    {
        IpAddress = ipAddress;
        Hostname = hostname;
        WasCached = wasCached;
    }
}
