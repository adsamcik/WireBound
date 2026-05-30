using AwesomeAssertions;
using WireBound.Core.Helpers;

namespace WireBound.Tests.Helpers;

public class TimeWindowedAverageTests
{
    [Test]
    public void Constructor_ZeroWindow_Throws()
    {
        var act = () => new TimeWindowedAverage(TimeSpan.Zero);
        act.Should().Throw<ArgumentOutOfRangeException>();
    }

    [Test]
    public void Constructor_NegativeWindow_Throws()
    {
        var act = () => new TimeWindowedAverage(TimeSpan.FromSeconds(-1));
        act.Should().Throw<ArgumentOutOfRangeException>();
    }

    [Test]
    public void GetAverage_NoSamples_ReturnsNaN()
    {
        var avg = new TimeWindowedAverage(TimeSpan.FromMinutes(1));
        avg.GetAverage(DateTime.UtcNow).Should().Be(double.NaN);
    }

    [Test]
    public void Add_SingleSample_AverageEqualsSample()
    {
        var avg = new TimeWindowedAverage(TimeSpan.FromMinutes(1));
        var now = new DateTime(2026, 1, 1, 12, 0, 0);

        avg.Add(42.0, now);

        avg.GetAverage(now).Should().Be(42.0);
        avg.Count.Should().Be(1);
    }

    [Test]
    public void Add_MultipleSamplesWithinWindow_AveragesAll()
    {
        var avg = new TimeWindowedAverage(TimeSpan.FromMinutes(1));
        var t0 = new DateTime(2026, 1, 1, 12, 0, 0);

        avg.Add(10.0, t0);
        avg.Add(20.0, t0.AddSeconds(10));
        avg.Add(30.0, t0.AddSeconds(20));

        avg.GetAverage(t0.AddSeconds(20)).Should().Be(20.0);
        avg.Count.Should().Be(3);
    }

    [Test]
    public void GetAverage_EvictsExpiredSamples()
    {
        var avg = new TimeWindowedAverage(TimeSpan.FromSeconds(60));
        var t0 = new DateTime(2026, 1, 1, 12, 0, 0);

        avg.Add(100.0, t0);
        avg.Add(50.0, t0.AddSeconds(30));
        avg.Add(20.0, t0.AddSeconds(80));

        // Reading at t0 + 90s: only 30s sample (50) and 80s sample (20) remain
        // (60s window from t=90 covers t=30 onwards).
        var result = avg.GetAverage(t0.AddSeconds(90));

        result.Should().Be(35.0);
        avg.Count.Should().Be(2);
    }

    [Test]
    public void GetAverage_AllSamplesExpired_ReturnsNaN()
    {
        var avg = new TimeWindowedAverage(TimeSpan.FromSeconds(30));
        var t0 = new DateTime(2026, 1, 1, 12, 0, 0);

        avg.Add(100.0, t0);
        avg.Add(50.0, t0.AddSeconds(10));

        var result = avg.GetAverage(t0.AddSeconds(120));

        result.Should().Be(double.NaN);
        avg.Count.Should().Be(0);
    }

    [Test]
    public void Add_AlsoEvictsExpiredSamples()
    {
        var avg = new TimeWindowedAverage(TimeSpan.FromSeconds(60));
        var t0 = new DateTime(2026, 1, 1, 12, 0, 0);

        avg.Add(100.0, t0);
        avg.Add(50.0, t0.AddSeconds(30));

        // Add another sample far in the future — should evict the first two.
        avg.Add(7.0, t0.AddSeconds(200));

        avg.Count.Should().Be(1);
        avg.GetAverage(t0.AddSeconds(200)).Should().Be(7.0);
    }
}
