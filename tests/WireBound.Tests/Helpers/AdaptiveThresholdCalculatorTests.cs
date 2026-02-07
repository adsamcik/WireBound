using WireBound.Core.Helpers;

namespace WireBound.Tests.Helpers;

/// <summary>
/// Unit tests for AdaptiveThresholdCalculator
/// </summary>
public class AdaptiveThresholdCalculatorTests
{
    // ═══════════════════════════════════════════════════════════════════════
    // Constructor Tests
    // ═══════════════════════════════════════════════════════════════════════

    [Test]
    public void Constructor_DefaultParams_CreatesValidInstance()
    {
        // Act
        var calculator = new AdaptiveThresholdCalculator();

        // Assert - should not throw, and GetThresholdLevels returns minimum
        var levels = calculator.GetThresholdLevels();
        levels.Full.Should().BeGreaterThanOrEqualTo(0);
    }

    [Test]
    public void Constructor_CustomParams_CreatesValidInstance()
    {
        // Act
        var calculator = new AdaptiveThresholdCalculator(windowSize: 30, smoothingFactor: 0.2);

        // Assert - should not throw
        var levels = calculator.GetThresholdLevels();
        levels.Full.Should().BeGreaterThanOrEqualTo(0);
    }

    // ═══════════════════════════════════════════════════════════════════════
    // Update Tests
    // ═══════════════════════════════════════════════════════════════════════

    [Test]
    public void Update_PositiveValue_ReturnsPositiveThreshold()
    {
        // Arrange
        var calculator = new AdaptiveThresholdCalculator();

        // Act
        var result = calculator.Update(5000);

        // Assert
        result.Should().BeGreaterThan(0);
    }

    [Test]
    public void Update_Zero_ReturnsMinimumThreshold()
    {
        // Arrange
        var calculator = new AdaptiveThresholdCalculator();

        // Act
        var result = calculator.Update(0);

        // Assert - minimum threshold is 1024 (1 KB/s)
        result.Should().Be(1024);
    }

    [Test]
    public void Update_MultipleValues_ReturnsPositive()
    {
        // Arrange
        var calculator = new AdaptiveThresholdCalculator();

        // Act
        calculator.Update(1000);
        calculator.Update(2000);
        var result = calculator.Update(3000);

        // Assert
        result.Should().BeGreaterThan(0);
    }

    // ═══════════════════════════════════════════════════════════════════════
    // RoundToNiceValue Tests
    // ═══════════════════════════════════════════════════════════════════════

    [Test]
    [Arguments(0, 1024)]
    [Arguments(-1, 1024)]
    [Arguments(-100, 1024)]
    public void RoundToNiceValue_ZeroOrNegative_Returns1024(double input, double expected)
    {
        // Act
        var result = AdaptiveThresholdCalculator.RoundToNiceValue(input);

        // Assert
        result.Should().Be(expected);
    }

    [Test]
    [Arguments(500, 512)]
    [Arguments(512, 512)]
    public void RoundToNiceValue_SmallValues_RoundsToSmallestNice(double input, double expected)
    {
        // Act
        var result = AdaptiveThresholdCalculator.RoundToNiceValue(input);

        // Assert
        result.Should().Be(expected);
    }

    [Test]
    public void RoundToNiceValue_1025_Returns5KB()
    {
        // Act
        var result = AdaptiveThresholdCalculator.RoundToNiceValue(1025);

        // Assert - 1025 > 1024, so next nice value is 5*1024 = 5120
        result.Should().Be(5 * 1024);
    }

    [Test]
    public void RoundToNiceValue_1_000_000_ReturnsExpectedNice()
    {
        // Act
        var result = AdaptiveThresholdCalculator.RoundToNiceValue(1_000_000);

        // Assert - 1,000,000 > 500*1024 (512000), so next is 1024*1024 (1,048,576)
        result.Should().Be(1024 * 1024);
    }

    [Test]
    public void RoundToNiceValue_ExactNiceValue_ReturnsSame()
    {
        // Act
        var result = AdaptiveThresholdCalculator.RoundToNiceValue(1024);

        // Assert
        result.Should().Be(1024);
    }

    [Test]
    public void RoundToNiceValue_VeryLarge_ReturnsLargestNice()
    {
        // Act - value larger than all nice values
        var result = AdaptiveThresholdCalculator.RoundToNiceValue(2.0 * 1024 * 1024 * 1024);

        // Assert - returns the largest nice value (1 GB/s)
        result.Should().Be(1024.0 * 1024 * 1024);
    }

    // ═══════════════════════════════════════════════════════════════════════
    // GetThresholdLevels Tests
    // ═══════════════════════════════════════════════════════════════════════

    [Test]
    public void GetThresholdLevels_AfterUpdate_ReturnsCorrectProportions()
    {
        // Arrange
        var calculator = new AdaptiveThresholdCalculator();
        calculator.Update(10000);

        // Act
        var levels = calculator.GetThresholdLevels();

        // Assert - proportions should be 25%, 50%, 75%, 100%
        levels.Quarter.Should().Be(levels.Full * 0.25);
        levels.Half.Should().Be(levels.Full * 0.5);
        levels.ThreeQuarter.Should().Be(levels.Full * 0.75);
    }

    [Test]
    public void GetThresholdLevels_NoUpdates_ReturnsMinimumBasedLevels()
    {
        // Arrange
        var calculator = new AdaptiveThresholdCalculator();

        // Act
        var levels = calculator.GetThresholdLevels();

        // Assert - smoothedThreshold is 0, RoundToNiceValue(0) = 1024
        levels.Full.Should().Be(1024);
        levels.Quarter.Should().Be(256);
        levels.Half.Should().Be(512);
        levels.ThreeQuarter.Should().Be(768);
    }

    // ═══════════════════════════════════════════════════════════════════════
    // Reset Tests
    // ═══════════════════════════════════════════════════════════════════════

    [Test]
    public void Reset_AfterUpdates_ClearsState()
    {
        // Arrange
        var calculator = new AdaptiveThresholdCalculator();
        calculator.Update(100_000);
        calculator.Update(200_000);

        // Act
        calculator.Reset();

        // Assert - should behave like a fresh instance
        var levels = calculator.GetThresholdLevels();
        levels.Full.Should().Be(1024);
    }

    [Test]
    public void Reset_ThenUpdate_WorksLikeNewInstance()
    {
        // Arrange
        var calculator = new AdaptiveThresholdCalculator();
        calculator.Update(1_000_000);
        calculator.Reset();

        // Act
        var result = calculator.Update(0);

        // Assert
        result.Should().Be(1024);
    }

    // ═══════════════════════════════════════════════════════════════════════
    // Rolling Window Tests
    // ═══════════════════════════════════════════════════════════════════════

    [Test]
    public void RollingWindow_TrimsToWindowSize()
    {
        // Arrange - window size of 5
        var calculator = new AdaptiveThresholdCalculator(windowSize: 5);

        // Act - add more than window size
        for (int i = 0; i < 10; i++)
        {
            calculator.Update(1000);
        }

        // Assert - should not throw and still work correctly
        var result = calculator.Update(1000);
        result.Should().BeGreaterThan(0);
    }

    // ═══════════════════════════════════════════════════════════════════════
    // Threshold Behavior Tests
    // ═══════════════════════════════════════════════════════════════════════

    [Test]
    public void Threshold_IncreasesQuickly_DecreasesSlowly()
    {
        // Arrange
        var calculator = new AdaptiveThresholdCalculator(windowSize: 60, smoothingFactor: 0.1);

        // Establish a baseline
        for (int i = 0; i < 10; i++)
        {
            calculator.Update(10_000);
        }
        var baseline = calculator.Update(10_000);

        // Act - spike up
        var afterSpike = calculator.Update(1_000_000);

        // Assert - threshold should increase quickly toward the spike
        afterSpike.Should().BeGreaterThan(baseline);

        // Now drop back down
        for (int i = 0; i < 5; i++)
        {
            calculator.Update(10_000);
        }
        var afterDrop = calculator.Update(10_000);

        // The threshold should still be elevated (slow decay)
        afterDrop.Should().BeGreaterThan(baseline);
    }
}
