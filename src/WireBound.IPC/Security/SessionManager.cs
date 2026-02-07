using System.Collections.Concurrent;

namespace WireBound.IPC.Security;

/// <summary>
/// Manages authenticated IPC sessions with expiry and concurrency limits.
/// Thread-safe for use from multiple client handler tasks.
/// </summary>
public sealed class SessionManager
{
    private readonly ConcurrentDictionary<string, SessionInfo> _sessions = new();
    private readonly SemaphoreSlim _createLock = new(1, 1);
    private readonly TimeSpan _maxDuration = IpcConstants.MaxSessionDuration;
    private readonly int _maxConcurrent = IpcConstants.MaxConcurrentSessions;

    /// <summary>
    /// Creates a new session for an authenticated client.
    /// Returns null if the maximum number of concurrent sessions is reached.
    /// Uses a lock to prevent TOCTOU race between count check and add.
    /// </summary>
    public SessionInfo? CreateSession(int clientPid, string executablePath)
    {
        CleanExpiredSessions();

        _createLock.Wait();
        try
        {
            if (_sessions.Count >= _maxConcurrent)
                return null;

            var session = new SessionInfo
            {
                SessionId = Guid.NewGuid().ToString("N"),
                ClientPid = clientPid,
                ExecutablePath = executablePath,
                CreatedAtUtc = DateTimeOffset.UtcNow,
                ExpiresAtUtc = DateTimeOffset.UtcNow.Add(_maxDuration)
            };

            return _sessions.TryAdd(session.SessionId, session) ? session : null;
        }
        finally
        {
            _createLock.Release();
        }
    }

    /// <summary>
    /// Validates that a session ID is active and not expired.
    /// Returns the session info if valid, null otherwise.
    /// </summary>
    public SessionInfo? ValidateSession(string? sessionId)
    {
        if (sessionId is null)
            return null;

        if (!_sessions.TryGetValue(sessionId, out var session))
            return null;

        if (DateTimeOffset.UtcNow >= session.ExpiresAtUtc)
        {
            _sessions.TryRemove(sessionId, out _);
            return null;
        }

        return session;
    }

    /// <summary>
    /// Removes a session (e.g., on client disconnect or shutdown).
    /// </summary>
    public bool RemoveSession(string sessionId) =>
        _sessions.TryRemove(sessionId, out _);

    public int ActiveCount => _sessions.Count;

    private void CleanExpiredSessions()
    {
        var now = DateTimeOffset.UtcNow;
        foreach (var kvp in _sessions)
        {
            if (now >= kvp.Value.ExpiresAtUtc)
                _sessions.TryRemove(kvp.Key, out _);
        }
    }
}

public sealed class SessionInfo
{
    public required string SessionId { get; init; }
    public required int ClientPid { get; init; }
    public required string ExecutablePath { get; init; }
    public required DateTimeOffset CreatedAtUtc { get; init; }
    public required DateTimeOffset ExpiresAtUtc { get; init; }
}
