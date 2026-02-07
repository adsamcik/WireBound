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

        // Use Task-based concurrency with countdown for tighter synchronization
        using var ready = new CountdownEvent(20);
        using var go = new ManualResetEventSlim(false);

        var tasks = Enumerable.Range(0, 20).Select(i => Task.Run(() =>
        {
            ready.Signal();
            go.Wait();
            results[i] = manager.CreateSession(i + 100, "/app");
        })).ToArray();

        ready.Wait();
        go.Set();
        Task.WaitAll(tasks, TimeSpan.FromSeconds(10)).Should().BeTrue("all tasks should complete within timeout");

        var created = results.Count(r => r is not null);
        created.Should().BeLessThanOrEqualTo(10, "should never exceed max concurrent sessions");
        created.Should().BeGreaterThan(0, "at least some sessions should be created");
    }
}
