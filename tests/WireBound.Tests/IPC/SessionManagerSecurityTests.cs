using WireBound.IPC.Security;

namespace WireBound.Tests.IPC;

public class SessionManagerSecurityTests
{
    [Test]
    public void CreateSession_ExceedingMaxConcurrent_ReturnsNull()
    {
        var manager = new SessionManager();

        // Create max sessions (IpcConstants.MaxConcurrentSessions = 10)
        for (var i = 0; i < 10; i++)
        {
            var session = manager.CreateSession(i + 100, "/app");
            session.Should().NotBeNull($"Session {i} should be created successfully");
        }

        // The 11th should fail
        var overflow = manager.CreateSession(999, "/app");
        overflow.Should().BeNull("max concurrent sessions exceeded");
    }

    [Test]
    public void CreateSession_ConcurrentCalls_NeverExceedsMax()
    {
        var manager = new SessionManager();
        var results = new SessionInfo?[20];
        var barrier = new Barrier(20);

        Parallel.For(0, 20, i =>
        {
            barrier.SignalAndWait();
            results[i] = manager.CreateSession(i + 100, "/app");
        });

        var created = results.Count(r => r is not null);
        created.Should().BeLessThanOrEqualTo(10, "should never exceed max concurrent sessions");
        created.Should().BeGreaterThan(0, "at least some sessions should be created");
    }
}
