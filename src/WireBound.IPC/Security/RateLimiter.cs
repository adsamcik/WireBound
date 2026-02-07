using System.Collections.Concurrent;

namespace WireBound.IPC.Security;

/// <summary>
/// Sliding-window rate limiter that tracks requests per client (by session ID).
/// Thread-safe for use from multiple client handler tasks.
/// </summary>
public sealed class RateLimiter
{
    private readonly int _maxRequestsPerSecond;
    private readonly ConcurrentDictionary<string, ClientWindow> _windows = new();

    public RateLimiter(int maxRequestsPerSecond = IpcConstants.MaxRequestsPerSecond)
    {
        _maxRequestsPerSecond = maxRequestsPerSecond;
    }

    /// <summary>
    /// Checks whether a request from the given session should be allowed.
    /// Returns true if allowed, false if rate-limited.
    /// </summary>
    public bool TryAcquire(string sessionId)
    {
        var window = _windows.GetOrAdd(sessionId, _ => new ClientWindow());
        return window.TryAcquire(_maxRequestsPerSecond);
    }

    /// <summary>
    /// Removes tracking for a session (call on disconnect).
    /// </summary>
    public void RemoveClient(string sessionId) =>
        _windows.TryRemove(sessionId, out _);

    private sealed class ClientWindow
    {
        private long _windowStart;
        private int _count;
        private readonly object _lock = new();

        public bool TryAcquire(int maxPerSecond)
        {
            var now = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();

            lock (_lock)
            {
                var windowStart = Interlocked.Read(ref _windowStart);

                // Start new window if current one expired
                if (now - windowStart >= 1000)
                {
                    Interlocked.Exchange(ref _windowStart, now);
                    _count = 1;
                    return true;
                }

                if (_count >= maxPerSecond)
                    return false;

                _count++;
                return true;
            }
        }
    }
}
