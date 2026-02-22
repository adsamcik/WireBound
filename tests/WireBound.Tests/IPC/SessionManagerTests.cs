using System.Reflection;
using WireBound.IPC.Security;

namespace WireBound.Tests.IPC;

public class SessionManagerTests
{
    [Test]
    public void CreateSession_ReturnsSessionInfo()
    {
        var manager = new SessionManager();
        var session = manager.CreateSession(1234, "/usr/bin/wirebound");

        session.Should().NotBeNull();
        session!.SessionId.Should().NotBeEmpty();
        session.ClientPid.Should().Be(1234);
        session.ExecutablePath.Should().Be("/usr/bin/wirebound");
    }

    [Test]
    public void CreateSession_MultipleCallsReturnDifferentIds()
    {
        var manager = new SessionManager();
        var session1 = manager.CreateSession(1234, "/app");
        var session2 = manager.CreateSession(5678, "/app");

        session1!.SessionId.Should().NotBe(session2!.SessionId);
    }

    [Test]
    public void ValidateSession_ValidSession_ReturnsSessionInfo()
    {
        var manager = new SessionManager();
        var session = manager.CreateSession(1234, "/app");

        var validated = manager.ValidateSession(session!.SessionId);
        validated.Should().NotBeNull();
        validated!.ClientPid.Should().Be(1234);
    }

    [Test]
    public void ValidateSession_InvalidId_ReturnsNull()
    {
        var manager = new SessionManager();
        manager.ValidateSession("nonexistent").Should().BeNull();
    }

    [Test]
    public void ValidateSession_NullId_ReturnsNull()
    {
        var manager = new SessionManager();
        manager.ValidateSession(null).Should().BeNull();
    }

    [Test]
    public void RemoveSession_RemovesFromActive()
    {
        var manager = new SessionManager();
        var session = manager.CreateSession(1234, "/app");

        manager.RemoveSession(session!.SessionId).Should().BeTrue();
        manager.ValidateSession(session.SessionId).Should().BeNull();
    }

    [Test]
    public void ActiveCount_TracksCorrectly()
    {
        var manager = new SessionManager();
        manager.ActiveCount.Should().Be(0);

        var s1 = manager.CreateSession(1, "/app");
        manager.ActiveCount.Should().Be(1);

        var s2 = manager.CreateSession(2, "/app");
        manager.ActiveCount.Should().Be(2);

        manager.RemoveSession(s1!.SessionId);
        manager.ActiveCount.Should().Be(1);
    }

    [Test]
    public void ValidateSession_ExpiredSession_ReturnsNull()
    {
        // Arrange
        var manager = new SessionManager();
        var session = manager.CreateSession(1234, "/app");
        session.Should().NotBeNull();

        // Use reflection to set ExpiresAtUtc to the past
        var expiresProperty = typeof(SessionInfo).GetProperty(nameof(SessionInfo.ExpiresAtUtc));
        expiresProperty.Should().NotBeNull();
        expiresProperty!.SetValue(session, DateTimeOffset.UtcNow.AddSeconds(-1));

        // Act
        var result = manager.ValidateSession(session!.SessionId);

        // Assert - expired session should be rejected
        result.Should().BeNull();
    }

    [Test]
    public void ValidateSession_ExpiredSession_IsRemovedFromActiveSessions()
    {
        // Arrange
        var manager = new SessionManager();
        var session = manager.CreateSession(1234, "/app");
        session.Should().NotBeNull();

        // Expire the session via reflection
        var expiresProperty = typeof(SessionInfo).GetProperty(nameof(SessionInfo.ExpiresAtUtc));
        expiresProperty!.SetValue(session, DateTimeOffset.UtcNow.AddSeconds(-1));

        // Act - validate to trigger removal
        manager.ValidateSession(session!.SessionId);

        // Assert - second validation should also return null (session was removed)
        manager.ValidateSession(session.SessionId).Should().BeNull();
    }
}
