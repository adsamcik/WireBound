using LiveChartsCore.Defaults;
using WireBound.Avalonia.Helpers;
using WireBound.Tests.Fixtures;

namespace WireBound.Tests.Helpers;

/// <summary>
/// Unit tests for ChartCollectionHelper trim operations.
/// </summary>
public class ChartCollectionHelperTests
{
    public ChartCollectionHelperTests()
    {
        LiveChartsHook.EnsureInitialized();
    }

    private static DateTimePoint Point(DateTime dt, double value = 0) => new(dt, value);

    // ═══════════════════════════════════════════════════════════════════════
    // TrimBeforeCutoff Tests
    // ═══════════════════════════════════════════════════════════════════════

    [Test]
    public void TrimBeforeCutoff_EmptyCollection_DoesNothing()
    {
        // Arrange
        var points = new BatchObservableCollection<DateTimePoint>();
        var cutoff = DateTime.Now;

        // Act
        ChartCollectionHelper.TrimBeforeCutoff(points, cutoff);

        // Assert
        points.Count.Should().Be(0);
    }

    [Test]
    public void TrimBeforeCutoff_AllPointsWithinRange_NoChange()
    {
        // Arrange
        var now = DateTime.Now;
        var points = new BatchObservableCollection<DateTimePoint>();
        points.Add(Point(now));
        points.Add(Point(now.AddSeconds(1)));
        points.Add(Point(now.AddSeconds(2)));
        var cutoff = now.AddSeconds(-1);

        // Act
        ChartCollectionHelper.TrimBeforeCutoff(points, cutoff);

        // Assert
        points.Count.Should().Be(3);
    }

    [Test]
    public void TrimBeforeCutoff_RemovesSingleOldPoint_UsesRemoveAt()
    {
        // Arrange — single old point triggers the fast RemoveAt(0) path
        var now = DateTime.Now;
        var points = new BatchObservableCollection<DateTimePoint>();
        points.Add(Point(now.AddSeconds(-10)));
        points.Add(Point(now));
        points.Add(Point(now.AddSeconds(1)));
        var cutoff = now;

        // Act
        ChartCollectionHelper.TrimBeforeCutoff(points, cutoff);

        // Assert — count reduced by exactly 1
        points.Count.Should().Be(2);
        points[0].DateTime.Should().Be(now);
    }

    [Test]
    public void TrimBeforeCutoff_RemovesMultipleOldPoints_KeepsRecent()
    {
        // Arrange
        var now = DateTime.Now;
        var points = new BatchObservableCollection<DateTimePoint>();
        points.Add(Point(now.AddSeconds(-30), 1));
        points.Add(Point(now.AddSeconds(-20), 2));
        points.Add(Point(now.AddSeconds(-10), 3));
        points.Add(Point(now, 4));
        points.Add(Point(now.AddSeconds(5), 5));
        var cutoff = now;

        // Act
        ChartCollectionHelper.TrimBeforeCutoff(points, cutoff);

        // Assert
        points.Count.Should().Be(2);
        points[0].Value.Should().Be(4);
        points[1].Value.Should().Be(5);
    }

    [Test]
    public void TrimBeforeCutoff_AllPointsOlderThanCutoff_ClearsCollection()
    {
        // Arrange
        var now = DateTime.Now;
        var points = new BatchObservableCollection<DateTimePoint>();
        points.Add(Point(now.AddMinutes(-5)));
        points.Add(Point(now.AddMinutes(-3)));
        points.Add(Point(now.AddMinutes(-1)));
        var cutoff = now;

        // Act
        ChartCollectionHelper.TrimBeforeCutoff(points, cutoff);

        // Assert
        points.Count.Should().Be(0);
    }

    [Test]
    public void TrimBeforeCutoff_FirstPointExactlyAtCutoff_IsKept()
    {
        // Arrange — the condition is >= cutoff, so exactly-at-cutoff is kept
        var now = DateTime.Now;
        var points = new BatchObservableCollection<DateTimePoint>();
        points.Add(Point(now, 1));
        points.Add(Point(now.AddSeconds(5), 2));
        var cutoff = now;

        // Act
        ChartCollectionHelper.TrimBeforeCutoff(points, cutoff);

        // Assert
        points.Count.Should().Be(2);
        points[0].Value.Should().Be(1);
    }

    // ═══════════════════════════════════════════════════════════════════════
    // TrimToMaxCount Tests
    // ═══════════════════════════════════════════════════════════════════════

    [Test]
    public void TrimToMaxCount_BelowMax_NoChange()
    {
        // Arrange
        var now = DateTime.Now;
        var points = new BatchObservableCollection<DateTimePoint>();
        points.Add(Point(now, 1));
        points.Add(Point(now.AddSeconds(1), 2));

        // Act
        ChartCollectionHelper.TrimToMaxCount(points, 5);

        // Assert
        points.Count.Should().Be(2);
    }

    [Test]
    public void TrimToMaxCount_ExactlyAtMax_NoChange()
    {
        // Arrange
        var now = DateTime.Now;
        var points = new BatchObservableCollection<DateTimePoint>();
        points.Add(Point(now, 1));
        points.Add(Point(now.AddSeconds(1), 2));
        points.Add(Point(now.AddSeconds(2), 3));

        // Act
        ChartCollectionHelper.TrimToMaxCount(points, 3);

        // Assert
        points.Count.Should().Be(3);
        points[0].Value.Should().Be(1);
    }

    [Test]
    public void TrimToMaxCount_AboveMax_TrimsToMax()
    {
        // Arrange
        var now = DateTime.Now;
        var points = new BatchObservableCollection<DateTimePoint>();
        for (var i = 0; i < 10; i++)
            points.Add(Point(now.AddSeconds(i), i));

        // Act
        ChartCollectionHelper.TrimToMaxCount(points, 5);

        // Assert
        points.Count.Should().Be(5);
    }

    [Test]
    public void TrimToMaxCount_KeepsNewestPoints()
    {
        // Arrange — oldest points (indices 0-4) should be removed, keeping 5-9
        var now = DateTime.Now;
        var points = new BatchObservableCollection<DateTimePoint>();
        for (var i = 0; i < 10; i++)
            points.Add(Point(now.AddSeconds(i), i));

        // Act
        ChartCollectionHelper.TrimToMaxCount(points, 5);

        // Assert — values 5 through 9 should remain
        points[0].Value.Should().Be(5);
        points[1].Value.Should().Be(6);
        points[2].Value.Should().Be(7);
        points[3].Value.Should().Be(8);
        points[4].Value.Should().Be(9);
    }
}
