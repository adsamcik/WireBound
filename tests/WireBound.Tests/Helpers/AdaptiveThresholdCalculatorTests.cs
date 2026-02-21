using System.Reflection;

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

    // ═══════════════════════════════════════════════════════════════════════
    // Asymmetric Rate Tests
    // ═══════════════════════════════════════════════════════════════════════

    [Test]
    public void Update_AfterSpike_MovesExactly30PercentTowardNewValue()
    {
        // Arrange - windowSize=1 so rollingMax always equals the last value
        var calculator = new AdaptiveThresholdCalculator(windowSize: 1, smoothingFactor: 0.1);
        calculator.Update(10_000); // smoothedThreshold initialized to 10000
        var baseline = GetSmoothedThreshold(calculator);

        // Act - spike to 100_000
        calculator.Update(100_000);
        var afterSpike = GetSmoothedThreshold(calculator);

        // Assert - increase formula: old * 0.7 + new * 0.3
        // Expected: 10000 * 0.7 + 100000 * 0.3 = 7000 + 30000 = 37000
        afterSpike.Should().BeApproximately(37_000, 0.01);

        var moveRatio = (afterSpike - baseline) / (100_000 - baseline);
        moveRatio.Should().BeApproximately(0.3, 0.001);
    }

    [Test]
    public void Update_AfterDrop_MovesExactlySmoothingFactorTowardNewValue()
    {
        // Arrange - windowSize=1 so rollingMax always equals the last value
        var calculator = new AdaptiveThresholdCalculator(windowSize: 1, smoothingFactor: 0.1);
        calculator.Update(100_000); // smoothedThreshold initialized to 100000
        var baseline = GetSmoothedThreshold(calculator);

        // Act - drop to 10_000
        calculator.Update(10_000);
        var afterDrop = GetSmoothedThreshold(calculator);

        // Assert - decrease formula: old * (1 - 0.1) + new * 0.1
        // Expected: 100000 * 0.9 + 10000 * 0.1 = 90000 + 1000 = 91000
        afterDrop.Should().BeApproximately(91_000, 0.01);

        var moveRatio = (baseline - afterDrop) / (baseline - 10_000);
        moveRatio.Should().BeApproximately(0.1, 0.001);
    }

    [Test]
    public void Update_IncreaseRate_Is3xFasterThanDecreaseRate()
    {
        // Arrange - two identical calculators
        var increaseCalc = new AdaptiveThresholdCalculator(windowSize: 1, smoothingFactor: 0.1);
        var decreaseCalc = new AdaptiveThresholdCalculator(windowSize: 1, smoothingFactor: 0.1);

        increaseCalc.Update(10_000);  // baseline at 10000
        decreaseCalc.Update(100_000); // baseline at 100000

        // Act - move both toward opposite extremes over same distance (90000)
        increaseCalc.Update(100_000); // increase by 90000
        decreaseCalc.Update(10_000);  // decrease by 90000

        var increaseMove = GetSmoothedThreshold(increaseCalc) - 10_000;
        var decreaseMove = 100_000 - GetSmoothedThreshold(decreaseCalc);

        // Assert - increase rate (0.3) should be exactly 3x the decrease rate (0.1)
        var ratio = increaseMove / decreaseMove;
        ratio.Should().BeApproximately(3.0, 0.001);
    }

    [Test]
    public void Update_ConsecutiveSpikes_AccumulateWithIncreaseFormula()
    {
        // Arrange - windowSize=1 so rollingMax always equals the last value
        var calculator = new AdaptiveThresholdCalculator(windowSize: 1, smoothingFactor: 0.1);
        calculator.Update(10_000); // smoothedThreshold = 10000

        // Act - two consecutive spikes
        calculator.Update(100_000); // 10000 * 0.7 + 100000 * 0.3 = 37000
        calculator.Update(100_000); // 37000 * 0.7 + 100000 * 0.3 = 25900 + 30000 = 55900

        // Assert
        GetSmoothedThreshold(calculator).Should().BeApproximately(55_900, 0.01);
    }

    [Test]
    public void Update_ConsecutiveDrops_DecayWithSmoothingFactor()
    {
        // Arrange - windowSize=1 so rollingMax always equals the last value
        var calculator = new AdaptiveThresholdCalculator(windowSize: 1, smoothingFactor: 0.1);
        calculator.Update(100_000); // smoothedThreshold = 100000

        // Act - two consecutive drops
        calculator.Update(10_000); // 100000 * 0.9 + 10000 * 0.1 = 91000
        calculator.Update(10_000); // 91000 * 0.9 + 10000 * 0.1 = 81900 + 1000 = 82900

        // Assert
        GetSmoothedThreshold(calculator).Should().BeApproximately(82_900, 0.01);
    }

    private static double GetSmoothedThreshold(AdaptiveThresholdCalculator calculator)
    {
        var field = typeof(AdaptiveThresholdCalculator)
            .GetField("_smoothedThreshold", BindingFlags.NonPublic | BindingFlags.Instance)!;
        return (double)field.GetValue(calculator)!;
    }
}
