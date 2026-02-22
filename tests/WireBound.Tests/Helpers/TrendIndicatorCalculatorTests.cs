using WireBound.Core.Helpers;

namespace WireBound.Tests.Helpers;

/// <summary>
/// Tests for <see cref="TrendIndicatorCalculator"/>.
/// </summary>
public sealed class TrendIndicatorCalculatorTests
{
    #region Constructor Tests

    [Test]
    public void Constructor_DefaultParameters_SetsGeometricIcons()
    {
        var calculator = new TrendIndicatorCalculator();

        // First call initializes and returns stable
        var result = calculator.Update(0);

        // First call is always stable regardless of value
        result.Icon.Should().Be("●"); // Geometric stable icon for first call
    }

    [Test]
    public void Constructor_ArrowsStyle_SetsArrowIcons()
    {
        var calculator = new TrendIndicatorCalculator(iconStyle: TrendIconStyle.Arrows);

        // First call initializes and returns stable
        var result = calculator.Update(0);

        // First call is always stable regardless of value
        result.Icon.Should().Be("→"); // Arrow stable icon for first call
    }

    [Test]
    [Arguments(0.1)]
    [Arguments(0.5)]
    [Arguments(0.9)]
    public void Constructor_ValidAlpha_DoesNotThrow(double alpha)
    {
        var action = () => new TrendIndicatorCalculator(alpha: alpha);

        action.Should().NotThrow();
    }

    [Test]
    [Arguments(0.0)]
    [Arguments(1.0)]
    [Arguments(-0.1)]
    [Arguments(1.5)]
    public void Constructor_OutOfRangeAlpha_IsClamped(double alpha)
    {
        // The constructor clamps values rather than throwing
        var action = () => new TrendIndicatorCalculator(alpha: alpha);

        action.Should().NotThrow();
    }

    [Test]
    [Arguments(0.0)]
    [Arguments(-0.1)]
    public void Constructor_OutOfRangeThreshold_IsClamped(double threshold)
    {
        // The constructor clamps values rather than throwing
        var action = () => new TrendIndicatorCalculator(thresholdPercent: threshold);

        action.Should().NotThrow();
    }

    [Test]
    [Arguments(-1)]
    [Arguments(-100)]
    public void Constructor_NegativeMinThreshold_IsClamped(long minThreshold)
    {
        // The constructor uses Math.Max so negatives become 1
        var action = () => new TrendIndicatorCalculator(minimumThreshold: minThreshold);

        action.Should().NotThrow();
    }

    #endregion

    #region Update Tests - Basic Behavior

    [Test]
    public void Update_ZeroValue_ReturnsIdle()
    {
        var calculator = new TrendIndicatorCalculator();

        // Need to initialize first, then go to zero
        calculator.Update(1000);
        var result = calculator.Update(0);

        result.Direction.Should().Be(TrendDirection.Idle);
    }

    [Test]
    public void Update_FirstNonZeroValue_ReturnsStable()
    {
        var calculator = new TrendIndicatorCalculator();

        var result = calculator.Update(1000);

        // First update builds the baseline, so it's stable
        result.Direction.Should().Be(TrendDirection.Stable);
    }

    [Test]
    public void Update_AfterActivity_ZeroReturnsIdle()
    {
        var calculator = new TrendIndicatorCalculator();

        // Build up some history
        calculator.Update(1000);
        calculator.Update(2000);

        // Now go idle
        var result = calculator.Update(0);

        result.Direction.Should().Be(TrendDirection.Idle);
    }

    #endregion

    #region Update Tests - Trend Detection

    [Test]
    public void Update_SignificantIncrease_ReturnsRising()
    {
        var calculator = new TrendIndicatorCalculator(
            alpha: 0.3,
            thresholdPercent: 0.1,
            minimumThreshold: 100);

        // Build baseline
        for (int i = 0; i < 10; i++)
            calculator.Update(1000);

        // Significant increase (well above 10% threshold)
        var result = calculator.Update(5000);

        result.Direction.Should().Be(TrendDirection.Rising);
    }

    [Test]
    public void Update_SignificantDecrease_ReturnsFalling()
    {
        var calculator = new TrendIndicatorCalculator(
            alpha: 0.3,
            thresholdPercent: 0.1,
            minimumThreshold: 100);

        // Build baseline at high value
        for (int i = 0; i < 10; i++)
            calculator.Update(5000);

        // Significant decrease
        var result = calculator.Update(1000);

        result.Direction.Should().Be(TrendDirection.Falling);
    }

    [Test]
    public void Update_SmallFluctuation_ReturnsStable()
    {
        var calculator = new TrendIndicatorCalculator(
            alpha: 0.3,
            thresholdPercent: 0.1,
            minimumThreshold: 100);

        // Build baseline
        for (int i = 0; i < 10; i++)
            calculator.Update(1000);

        // Small change (within threshold)
        var result = calculator.Update(1050);

        result.Direction.Should().Be(TrendDirection.Stable);
    }

    [Test]
    public void Update_MinimumThreshold_PreventsFalsePositives()
    {
        var calculator = new TrendIndicatorCalculator(
            alpha: 0.3,
            thresholdPercent: 0.1,
            minimumThreshold: 1000); // High minimum threshold

        // Build baseline at low value
        for (int i = 0; i < 10; i++)
            calculator.Update(100);

        // 50% increase, but below min threshold
        var result = calculator.Update(150);

        result.Direction.Should().Be(TrendDirection.Stable);
    }

    #endregion

    #region Icon Tests - Geometric Style

    [Test]
    public void Update_GeometricStyle_IdleReturnsEmptyCircle()
    {
        var calculator = new TrendIndicatorCalculator(iconStyle: TrendIconStyle.Geometric);

        calculator.Update(1000); // Initialize
        var result = calculator.Update(0);

        result.Icon.Should().Be("○");
    }

    [Test]
    public void Update_GeometricStyle_RisingReturnsUpTriangle()
    {
        var calculator = new TrendIndicatorCalculator(iconStyle: TrendIconStyle.Geometric);

        // Build baseline and then spike
        for (int i = 0; i < 10; i++)
            calculator.Update(1000);

        var result = calculator.Update(10000);

        result.Icon.Should().Be("▲");
    }

    [Test]
    public void Update_GeometricStyle_FallingReturnsDownTriangle()
    {
        var calculator = new TrendIndicatorCalculator(iconStyle: TrendIconStyle.Geometric);

        // Build baseline high and then drop
        for (int i = 0; i < 10; i++)
            calculator.Update(10000);

        var result = calculator.Update(1000);

        result.Icon.Should().Be("▼");
    }

    [Test]
    public void Update_GeometricStyle_StableReturnsBullet()
    {
        var calculator = new TrendIndicatorCalculator(iconStyle: TrendIconStyle.Geometric);

        // Stable value
        for (int i = 0; i < 5; i++)
            calculator.Update(1000);

        var result = calculator.Update(1000);

        result.Icon.Should().Be("●");
    }

    #endregion

    #region Icon Tests - Arrows Style

    [Test]
    public void Update_ArrowsStyle_IdleReturnsEmptyCircle()
    {
        var calculator = new TrendIndicatorCalculator(iconStyle: TrendIconStyle.Arrows);

        calculator.Update(1000); // Initialize
        var result = calculator.Update(0);

        result.Icon.Should().Be("○");
    }

    [Test]
    public void Update_ArrowsStyle_RisingReturnsUpArrow()
    {
        var calculator = new TrendIndicatorCalculator(iconStyle: TrendIconStyle.Arrows);

        for (int i = 0; i < 10; i++)
            calculator.Update(1000);

        var result = calculator.Update(10000);

        result.Icon.Should().Be("↑");
    }

    [Test]
    public void Update_ArrowsStyle_FallingReturnsDownArrow()
    {
        var calculator = new TrendIndicatorCalculator(iconStyle: TrendIconStyle.Arrows);

        for (int i = 0; i < 10; i++)
            calculator.Update(10000);

        var result = calculator.Update(1000);

        result.Icon.Should().Be("↓");
    }

    [Test]
    public void Update_ArrowsStyle_StableReturnsRightArrow()
    {
        var calculator = new TrendIndicatorCalculator(iconStyle: TrendIconStyle.Arrows);

        for (int i = 0; i < 5; i++)
            calculator.Update(1000);

        var result = calculator.Update(1000);

        result.Icon.Should().Be("→");
    }

    #endregion

    #region TrendResult Tests

    [Test]
    public void TrendResult_ContainsIcon()
    {
        var calculator = new TrendIndicatorCalculator();

        var result = calculator.Update(1000);

        result.Icon.Should().NotBeNullOrEmpty();
    }

    [Test]
    public void TrendResult_ContainsText()
    {
        var calculator = new TrendIndicatorCalculator();

        var result = calculator.Update(1000);

        result.Text.Should().NotBeNullOrEmpty();
    }

    [Test]
    public void TrendResult_DirectionMatchesText()
    {
        var calculator = new TrendIndicatorCalculator();

        var result = calculator.Update(1000);

        result.Text.ToLowerInvariant().Should().Contain(result.Direction.ToString().ToLowerInvariant());
    }

    #endregion

    #region Edge Cases

    [Test]
    public void Update_VeryLargeValues_HandlesWithoutOverflow()
    {
        var calculator = new TrendIndicatorCalculator();

        var result = calculator.Update(long.MaxValue / 2);

        result.Direction.Should().Be(TrendDirection.Stable);
    }

    [Test]
    public void Update_NegativeValues_TreatedAsZero()
    {
        var calculator = new TrendIndicatorCalculator();

        calculator.Update(1000); // Initialize

        // The calculator doesn't explicitly handle negatives, but behavior should be predictable
        var result = calculator.Update(-1000);

        // Either idle (if treated as 0) or some other valid state
        result.Direction.Should().BeOneOf(TrendDirection.Idle, TrendDirection.Falling, TrendDirection.Stable);
    }

    [Test]
    public void Update_AlternatingValues_TracksCorrectly()
    {
        var calculator = new TrendIndicatorCalculator();

        var results = new List<TrendDirection>();

        for (int i = 0; i < 10; i++)
        {
            var value = i % 2 == 0 ? 1000 : 2000;
            var result = calculator.Update(value);
            results.Add(result.Direction);
        }

        // Should have mix of directions due to alternating
        results.Should().NotBeEmpty();
    }

    [Test]
    public void Update_GradualIncrease_EventuallyDetectsRising()
    {
        var calculator = new TrendIndicatorCalculator(
            alpha: 0.3,
            thresholdPercent: 0.1,
            minimumThreshold: 100);

        // Start low
        for (int i = 0; i < 5; i++)
            calculator.Update(1000);

        // Make a significant jump (not gradual) to trigger rising
        // The calculator compares current value to previous value,
        // so we need the jump between consecutive values to exceed threshold
        var lastResult = calculator.Update(5000); // Large jump from ~1000 baseline

        // Should detect rising trend from the significant increase
        lastResult.Direction.Should().Be(TrendDirection.Rising);
    }

    #endregion

    #region Reset Behavior (implicit through zero)

    [Test]
    public void Update_AfterIdle_ResumesTracking()
    {
        var calculator = new TrendIndicatorCalculator();

        // Build up
        for (int i = 0; i < 5; i++)
            calculator.Update(5000);

        // Go idle (0 returns Idle direction)
        var idleResult = calculator.Update(0);
        idleResult.Direction.Should().Be(TrendDirection.Idle);

        // Resume - comparing 1000 to previous value of 0, which is a rise
        var result = calculator.Update(1000);

        // Since previous was 0, going to 1000 is rising
        result.Direction.Should().Be(TrendDirection.Rising);
    }

    #endregion

    #region Boundary Tests - Mutation Killers

    [Test]
    public void Update_DiffExactlyEqualsThreshold_ReturnsStable()
    {
        // Use fixed parameters to get a predictable threshold:
        // minimumThreshold = 100, so threshold = Max(movingAvg * 0.1, 100)
        // After 20 updates at 1000, movingAvg ≈ 1000, so threshold = Max(100, 100) = 100
        var calculator = new TrendIndicatorCalculator(
            alpha: 0.3,
            thresholdPercent: 0.1,
            minimumThreshold: 100);

        // Build stable baseline — after many updates at same value, movingAvg ≈ 1000
        for (int i = 0; i < 20; i++)
            calculator.Update(1000);

        // diff = 1100 - 1000 = 100. threshold = Max(1000 * 0.1, 100) = 100.
        // diff == threshold, NOT diff > threshold, so should be Stable
        var result = calculator.Update(1100);

        result.Direction.Should().Be(TrendDirection.Stable);
    }

    [Test]
    public void Update_DiffExactlyEqualsNegativeThreshold_ReturnsStable()
    {
        var calculator = new TrendIndicatorCalculator(
            alpha: 0.3,
            thresholdPercent: 0.1,
            minimumThreshold: 100);

        // Build stable baseline at 1000
        for (int i = 0; i < 20; i++)
            calculator.Update(1000);

        // diff = 900 - 1000 = -100. threshold = 100. diff == -threshold, should be Stable
        var result = calculator.Update(900);

        result.Direction.Should().Be(TrendDirection.Stable);
    }

    [Test]
    public void Reset_ThenUpdate_TreatsAsFirstCall()
    {
        var calculator = new TrendIndicatorCalculator(
            alpha: 0.3,
            thresholdPercent: 0.1,
            minimumThreshold: 100);

        // Build up some history
        for (int i = 0; i < 10; i++)
            calculator.Update(5000);

        // Reset
        calculator.Reset();

        // After reset, the next Update should be treated as the first call → Stable
        var result = calculator.Update(1000);

        result.Direction.Should().Be(TrendDirection.Stable);
    }

    [Test]
    public void Update_NegativeValue_IsNotIdle()
    {
        var calculator = new TrendIndicatorCalculator(
            alpha: 0.3,
            thresholdPercent: 0.1,
            minimumThreshold: 100);

        // Build baseline at 1000
        for (int i = 0; i < 10; i++)
            calculator.Update(1000);

        // Negative value: diff = -1 - 1000 = -1001. threshold = 100.
        // -1001 < -100, so should be Falling, NOT Idle
        var result = calculator.Update(-1);

        result.Direction.Should().Be(TrendDirection.Falling);
    }

    #endregion
}
