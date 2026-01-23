using System.Collections.Concurrent;
using System.Net;
using Microsoft.Extensions.Logging;
using WireBound.Platform.Abstract.Services;

namespace WireBound.Avalonia.Services;

/// <summary>
/// DNS resolver service with LRU caching for IP address to hostname resolution.
/// Performs async DNS lookups and caches results to avoid repeated lookups.
/// </summary>
public sealed class DnsResolverService : IDnsResolverService, IDisposable
{
    private readonly ConcurrentDictionary<string, DnsCacheEntry> _cache = new();
    private readonly ConcurrentQueue<string> _resolutionQueue = new();
    private readonly CancellationTokenSource _cts = new();
    private readonly Task _backgroundTask;
    private readonly SemaphoreSlim _resolutionSemaphore = new(5); // Max 5 concurrent lookups
    private readonly object _lruLock = new();
    private readonly LinkedList<string> _lruList = new();
    private readonly ILogger<DnsResolverService>? _logger;
    private bool _disposed;

    public int CacheSize => _cache.Count;
    public int MaxCacheSize { get; set; } = 1000;
    public TimeSpan CacheTtl { get; set; } = TimeSpan.FromMinutes(30);

    public event EventHandler<DnsResolvedEventArgs>? HostnameResolved;

    public DnsResolverService(ILogger<DnsResolverService>? logger = null)
    {
        _logger = logger;
        _backgroundTask = Task.Run(ProcessResolutionQueueAsync);
    }

    public async Task<string?> ResolveAsync(string ipAddress, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(ipAddress))
            return null;

        // Check cache first
        if (TryGetFromCache(ipAddress, out var hostname))
        {
            return hostname;
        }

        // Skip private/local addresses
        if (IsPrivateOrLocal(ipAddress))
        {
            AddToCache(ipAddress, null);
            return null;
        }

        try
        {
            await _resolutionSemaphore.WaitAsync(cancellationToken);
            try
            {
                // Double-check cache after acquiring semaphore
                if (TryGetFromCache(ipAddress, out hostname))
                {
                    return hostname;
                }

                var entry = await Dns.GetHostEntryAsync(ipAddress, cancellationToken);
                hostname = entry.HostName;
                
                // Don't cache if hostname is same as IP (failed resolution)
                if (hostname != ipAddress)
                {
                    AddToCache(ipAddress, hostname);
                    HostnameResolved?.Invoke(this, new DnsResolvedEventArgs(ipAddress, hostname, wasCached: false));
                }
                else
                {
                    AddToCache(ipAddress, null);
                }
                
                return hostname != ipAddress ? hostname : null;
            }
            finally
            {
                _resolutionSemaphore.Release();
            }
        }
        catch (Exception)
        {
            // Resolution failed - cache the failure
            AddToCache(ipAddress, null);
            return null;
        }
    }

    public string? GetCached(string ipAddress)
    {
        if (TryGetFromCache(ipAddress, out var hostname))
        {
            return hostname;
        }
        return null;
    }

    public void QueueForResolution(string ipAddress)
    {
        if (string.IsNullOrWhiteSpace(ipAddress))
            return;

        // Skip if already cached
        if (_cache.ContainsKey(ipAddress))
            return;

        // Skip private/local addresses
        if (IsPrivateOrLocal(ipAddress))
            return;

        _resolutionQueue.Enqueue(ipAddress);
    }

    public void ClearCache()
    {
        _cache.Clear();
        lock (_lruLock)
        {
            _lruList.Clear();
        }
    }

    private bool TryGetFromCache(string ipAddress, out string? hostname)
    {
        if (_cache.TryGetValue(ipAddress, out var entry))
        {
            if (DateTime.UtcNow - entry.CachedAt < CacheTtl)
            {
                // Update LRU
                TouchLru(ipAddress);
                hostname = entry.Hostname;
                return true;
            }
            else
            {
                // Expired - remove from cache
                _cache.TryRemove(ipAddress, out _);
                RemoveFromLru(ipAddress);
            }
        }
        hostname = null;
        return false;
    }

    private void AddToCache(string ipAddress, string? hostname)
    {
        // Evict oldest if at capacity
        while (_cache.Count >= MaxCacheSize)
        {
            EvictOldest();
        }

        _cache[ipAddress] = new DnsCacheEntry(hostname, DateTime.UtcNow);
        
        lock (_lruLock)
        {
            _lruList.AddFirst(ipAddress);
        }
    }

    private void TouchLru(string ipAddress)
    {
        lock (_lruLock)
        {
            var node = _lruList.Find(ipAddress);
            if (node != null)
            {
                _lruList.Remove(node);
                _lruList.AddFirst(node);
            }
        }
    }

    private void RemoveFromLru(string ipAddress)
    {
        lock (_lruLock)
        {
            _lruList.Remove(ipAddress);
        }
    }

    private void EvictOldest()
    {
        lock (_lruLock)
        {
            if (_lruList.Last != null)
            {
                var oldest = _lruList.Last.Value;
                _lruList.RemoveLast();
                _cache.TryRemove(oldest, out _);
            }
        }
    }

    private static bool IsPrivateOrLocal(string ipAddress)
    {
        if (!IPAddress.TryParse(ipAddress, out var ip))
            return true;

        if (IPAddress.IsLoopback(ip))
            return true;

        var bytes = ip.GetAddressBytes();

        // IPv4 private ranges
        if (bytes.Length == 4)
        {
            // 10.0.0.0/8
            if (bytes[0] == 10)
                return true;
            // 172.16.0.0/12
            if (bytes[0] == 172 && bytes[1] >= 16 && bytes[1] <= 31)
                return true;
            // 192.168.0.0/16
            if (bytes[0] == 192 && bytes[1] == 168)
                return true;
            // 169.254.0.0/16 (link-local)
            if (bytes[0] == 169 && bytes[1] == 254)
                return true;
            // 0.0.0.0
            if (bytes[0] == 0 && bytes[1] == 0 && bytes[2] == 0 && bytes[3] == 0)
                return true;
        }

        // IPv6 private/local
        if (ip.IsIPv6LinkLocal || ip.IsIPv6SiteLocal)
            return true;

        return false;
    }

    private async Task ProcessResolutionQueueAsync()
    {
        while (!_cts.Token.IsCancellationRequested)
        {
            try
            {
                if (_resolutionQueue.TryDequeue(out var ipAddress))
                {
                    if (!_cache.ContainsKey(ipAddress))
                    {
                        await ResolveAsync(ipAddress, _cts.Token);
                    }
                }
                else
                {
                    await Task.Delay(100, _cts.Token);
                }
            }
            catch (OperationCanceledException)
            {
                break;
            }
            catch
            {
                // Ignore errors in background processing
            }
        }
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;

        _cts.Cancel();
        try
        {
            _backgroundTask.Wait(TimeSpan.FromSeconds(2));
        }
        catch (Exception ex)
        {
            _logger?.LogDebug(ex, "Background task did not complete gracefully during disposal");
        }

        _cts.Dispose();
        _resolutionSemaphore.Dispose();
    }

    private record DnsCacheEntry(string? Hostname, DateTime CachedAt);
}
