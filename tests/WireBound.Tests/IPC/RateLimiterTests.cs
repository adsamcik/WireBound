using WireBound.IPC.Security;

namespace WireBound.Tests.IPC;

public class RateLimiterTests
{
    [Test]
    public void TryAcquire_UnderLimit_ReturnsTrue()
    {
        var limiter = new RateLimiter(maxRequestsPerSecond: 10);

        for (var i = 0; i < 10; i++)
        {
            limiter.TryAcquire("session1").Should().BeTrue();
        }
    }

    [Test]
    public void TryAcquire_OverLimit_ReturnsFalse()
    {
        var limiter = new RateLimiter(maxRequestsPerSecond: 5);

        for (var i = 0; i < 5; i++)
            limiter.TryAcquire("session1");

        limiter.TryAcquire("session1").Should().BeFalse();
    }

    [Test]
    public void TryAcquire_DifferentSessions_IndependentLimits()
    {
        var limiter = new RateLimiter(maxRequestsPerSecond: 2);

        limiter.TryAcquire("session1").Should().BeTrue();
        limiter.TryAcquire("session1").Should().BeTrue();
        limiter.TryAcquire("session1").Should().BeFalse();

        // Different session should still work
        limiter.TryAcquire("session2").Should().BeTrue();
    }

    [Test]
    public void RemoveClient_ClearsTracking()
    {
        var limiter = new RateLimiter(maxRequestsPerSecond: 1);

        limiter.TryAcquire("session1").Should().BeTrue();
        limiter.TryAcquire("session1").Should().BeFalse();

        limiter.RemoveClient("session1");

        // After removal, the session gets a fresh window
        limiter.TryAcquire("session1").Should().BeTrue();
    }
}
