using WireBound.IPC.Security;

namespace WireBound.Tests.IPC;

/// <summary>
/// Comprehensive SessionManager tests — expiry, cleanup, boundary conditions,
/// and edge cases for mutation testing.
/// </summary>
public class SessionManagerComprehensiveTests
{
    // ═══════════════════════════════════════════════════════════════════════
    // Session expiry
    // ═══════════════════════════════════════════════════════════════════════

    [Test]
    public void ValidateSession_WithNull_ReturnsNull()
    {
        var manager = new SessionManager();
        manager.ValidateSession(null).Should().BeNull();
    }

    [Test]
    public void ValidateSession_WithEmptyString_ReturnsNull()
    {
        var manager = new SessionManager();
        manager.ValidateSession("").Should().BeNull();
    }

    [Test]
    public void ValidateSession_WithRandomString_ReturnsNull()
    {
        var manager = new SessionManager();
        manager.ValidateSession("nonexistent-session-id").Should().BeNull();
    }

    [Test]
    public void ValidateSession_WithValidSession_ReturnsSessionInfo()
    {
        var manager = new SessionManager();
        var session = manager.CreateSession(100, "/app");
        session.Should().NotBeNull();

        var validated = manager.ValidateSession(session!.SessionId);
        validated.Should().NotBeNull();
        validated!.ClientPid.Should().Be(100);
        validated.ExecutablePath.Should().Be("/app");
    }

    // ═══════════════════════════════════════════════════════════════════════
    // Session properties
    // ═══════════════════════════════════════════════════════════════════════

    [Test]
    public void CreateSession_SetsAllProperties()
    {
        var manager = new SessionManager();
        var session = manager.CreateSession(42, "/usr/bin/wirebound");

        session.Should().NotBeNull();
        session!.SessionId.Should().NotBeNullOrEmpty();
        session.SessionId.Should().HaveLength(32, "should be Guid.ToString('N')");
        session.ClientPid.Should().Be(42);
        session.ExecutablePath.Should().Be("/usr/bin/wirebound");
        session.CreatedAtUtc.Should().BeCloseTo(DateTimeOffset.UtcNow, TimeSpan.FromSeconds(2));
        session.ExpiresAtUtc.Should().BeAfter(session.CreatedAtUtc);
    }

    [Test]
    public void CreateSession_GeneratesUniqueSessionIds()
    {
        var manager = new SessionManager();
        var ids = new HashSet<string>();

        for (var i = 0; i < 10; i++)
        {
            var session = manager.CreateSession(i, "/app");
            session.Should().NotBeNull();
            ids.Add(session!.SessionId).Should().BeTrue($"session {i} ID should be unique");
        }
    }

    // ═══════════════════════════════════════════════════════════════════════
    // RemoveSession
    // ═══════════════════════════════════════════════════════════════════════

    [Test]
    public void RemoveSession_ExistingSession_ReturnsTrue()
    {
        var manager = new SessionManager();
        var session = manager.CreateSession(1, "/app");
        manager.RemoveSession(session!.SessionId).Should().BeTrue();
    }

    [Test]
    public void RemoveSession_NonExistentSession_ReturnsFalse()
    {
        var manager = new SessionManager();
        manager.RemoveSession("does-not-exist").Should().BeFalse();
    }

    [Test]
    public void RemoveSession_DecrementsActiveCount()
    {
        var manager = new SessionManager();
        var session = manager.CreateSession(1, "/app");
        manager.ActiveCount.Should().Be(1);

        manager.RemoveSession(session!.SessionId);
        manager.ActiveCount.Should().Be(0);
    }

    [Test]
    public void RemoveSession_ThenValidate_ReturnsNull()
    {
        var manager = new SessionManager();
        var session = manager.CreateSession(1, "/app");
        manager.RemoveSession(session!.SessionId);

        manager.ValidateSession(session.SessionId).Should().BeNull();
    }

    // ═══════════════════════════════════════════════════════════════════════
    // ActiveCount
    // ═══════════════════════════════════════════════════════════════════════

    [Test]
    public void ActiveCount_InitiallyZero()
    {
        var manager = new SessionManager();
        manager.ActiveCount.Should().Be(0);
    }

    [Test]
    public void ActiveCount_AfterCreateAndRemoveCycles()
    {
        var manager = new SessionManager();

        var s1 = manager.CreateSession(1, "/app");
        var s2 = manager.CreateSession(2, "/app");
        var s3 = manager.CreateSession(3, "/app");
        manager.ActiveCount.Should().Be(3);

        manager.RemoveSession(s2!.SessionId);
        manager.ActiveCount.Should().Be(2);

        manager.RemoveSession(s1!.SessionId);
        manager.RemoveSession(s3!.SessionId);
        manager.ActiveCount.Should().Be(0);
    }

    // ═══════════════════════════════════════════════════════════════════════
    // Max concurrent sessions - boundary
    // ═══════════════════════════════════════════════════════════════════════

    [Test]
    public void CreateSession_AtExactMax_Succeeds()
    {
        var manager = new SessionManager();

        // Fill to exactly max (10)
        for (var i = 0; i < 10; i++)
        {
            var s = manager.CreateSession(i, "/app");
            s.Should().NotBeNull($"session {i} should succeed (within limit)");
        }

        manager.ActiveCount.Should().Be(10);
    }

    [Test]
    public void CreateSession_AtMaxPlusOne_ReturnsNull()
    {
        var manager = new SessionManager();

        for (var i = 0; i < 10; i++)
            manager.CreateSession(i, "/app");

        manager.CreateSession(99, "/app").Should().BeNull("max+1 should fail");
    }

    [Test]
    public void CreateSession_AfterRemoveFromFull_Succeeds()
    {
        var manager = new SessionManager();
        var sessions = new List<SessionInfo>();

        for (var i = 0; i < 10; i++)
            sessions.Add(manager.CreateSession(i, "/app")!);

        // Full — next should fail
        manager.CreateSession(99, "/app").Should().BeNull();

        // Remove one
        manager.RemoveSession(sessions[5].SessionId);

        // Now one slot is free
        var newSession = manager.CreateSession(99, "/app");
        newSession.Should().NotBeNull("one slot was freed");
    }

    // ═══════════════════════════════════════════════════════════════════════
    // Same PID multiple sessions
    // ═══════════════════════════════════════════════════════════════════════

    [Test]
    public void CreateSession_SamePidMultipleTimes_AllowsMultiple()
    {
        var manager = new SessionManager();

        var s1 = manager.CreateSession(100, "/app");
        var s2 = manager.CreateSession(100, "/app");

        s1.Should().NotBeNull();
        s2.Should().NotBeNull();
        s1!.SessionId.Should().NotBe(s2!.SessionId);
    }
}
