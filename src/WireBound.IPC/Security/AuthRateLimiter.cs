using System.Collections.Concurrent;

namespace WireBound.IPC.Security;

/// <summary>
/// Pre-authentication rate limiter that tracks auth attempts per client.
/// Limits attempts per second and disconnects after consecutive failures.
/// Thread-safe for use from multiple client handler tasks.
/// </summary>
public sealed class AuthRateLimiter
{
    private readonly int _maxAttemptsPerSecond;
    private readonly int _maxConsecutiveFailures;
    private readonly TimeProvider _timeProvider;
    private readonly ConcurrentDictionary<string, ClientAuthState> _clients = new();

    public AuthRateLimiter(
        int maxAttemptsPerSecond = IpcConstants.MaxAuthAttemptsPerSecond,
        int maxConsecutiveFailures = IpcConstants.MaxConsecutiveAuthFailures,
        TimeProvider? timeProvider = null)
    {
        _maxAttemptsPerSecond = maxAttemptsPerSecond;
        _maxConsecutiveFailures = maxConsecutiveFailures;
        _timeProvider = timeProvider ?? TimeProvider.System;
    }

    /// <summary>
    /// Checks whether an auth attempt from the given client should be allowed.
    /// Returns true if allowed, false if rate-limited.
    /// </summary>
    public bool TryAcquire(string clientId)
    {
        var state = _clients.GetOrAdd(clientId, _ => new ClientAuthState());
        return state.TryAcquire(_maxAttemptsPerSecond, _timeProvider);
    }

    /// <summary>
    /// Records an authentication failure. Returns true if the client should be disconnected
    /// (exceeded max consecutive failures).
    /// </summary>
    public bool RecordFailure(string clientId)
    {
        var state = _clients.GetOrAdd(clientId, _ => new ClientAuthState());
        return state.RecordFailure(_maxConsecutiveFailures);
    }

    /// <summary>
    /// Records a successful authentication, resetting the failure counter.
    /// </summary>
    public void RecordSuccess(string clientId)
    {
        if (_clients.TryGetValue(clientId, out var state))
            state.ResetFailures();
    }

    /// <summary>
    /// Removes tracking for a client (call on disconnect).
    /// </summary>
    public void RemoveClient(string clientId) =>
        _clients.TryRemove(clientId, out _);

    private sealed class ClientAuthState
    {
        private long _windowStart;
        private int _count;
        private int _consecutiveFailures;
        private readonly object _lock = new();

        public bool TryAcquire(int maxPerSecond, TimeProvider timeProvider)
        {
            lock (_lock)
            {
                var now = timeProvider.GetUtcNow().ToUnixTimeMilliseconds();

                if (now - _windowStart >= 1000)
                {
                    _windowStart = now;
                    _count = 1;
                    return true;
                }

                if (_count >= maxPerSecond)
                    return false;

                _count++;
                return true;
            }
        }

        public bool RecordFailure(int maxConsecutiveFailures)
        {
            return Interlocked.Increment(ref _consecutiveFailures) >= maxConsecutiveFailures;
        }

        public void ResetFailures()
        {
            Interlocked.Exchange(ref _consecutiveFailures, 0);
        }
    }
}
