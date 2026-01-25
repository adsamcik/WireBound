namespace WireBound.Core.Helpers;

/// <summary>
/// The trend direction for a metric.
/// </summary>
public enum TrendDirection
{
    /// <summary>No activity (value is zero)</summary>
    Idle,
    /// <summary>Value is increasing significantly</summary>
    Rising,
    /// <summary>Value is decreasing significantly</summary>
    Falling,
    /// <summary>Value is relatively stable</summary>
    Stable
}

/// <summary>
/// Represents the calculated trend for a metric.
/// </summary>
public readonly record struct TrendResult(
    TrendDirection Direction,
    string Icon,
    string Text);

/// <summary>
/// Icon style for trend indicators.
/// </summary>
public enum TrendIconStyle
{
    /// <summary>Geometric symbols: ▲ ▼ ● ○</summary>
    Geometric,
    /// <summary>Arrow symbols: ↑ ↓ →</summary>
    Arrows
}

/// <summary>
/// Calculates trend indicators using exponential moving average.
/// Shared logic for Dashboard and Overview views to reduce duplication.
/// </summary>
public class TrendIndicatorCalculator
{
    private readonly double _alpha;
    private readonly double _thresholdPercent;
    private readonly long _minimumThreshold;
    private readonly TrendIconStyle _iconStyle;
    
    private long _movingAverage;
    private long _previousValue;
    private bool _initialized;
    
    /// <summary>
    /// Creates a new trend indicator calculator.
    /// </summary>
    /// <param name="alpha">Smoothing factor for exponential moving average (0-1). Higher values react faster to changes.</param>
    /// <param name="thresholdPercent">Percentage of moving average to consider as significant change (0-1).</param>
    /// <param name="minimumThreshold">Minimum absolute threshold in bytes/second.</param>
    /// <param name="iconStyle">The icon style to use for trend indicators.</param>
    public TrendIndicatorCalculator(
        double alpha = 0.3,
        double thresholdPercent = 0.1,
        long minimumThreshold = 100,
        TrendIconStyle iconStyle = TrendIconStyle.Geometric)
    {
        _alpha = Math.Clamp(alpha, 0.01, 1.0);
        _thresholdPercent = Math.Clamp(thresholdPercent, 0.01, 1.0);
        _minimumThreshold = Math.Max(minimumThreshold, 1);
        _iconStyle = iconStyle;
    }
    
    /// <summary>
    /// Updates the trend with a new value and returns the calculated trend.
    /// </summary>
    /// <param name="currentValue">The current value (e.g., bytes per second).</param>
    /// <returns>The trend result including direction, icon, and text.</returns>
    public TrendResult Update(long currentValue)
    {
        if (!_initialized)
        {
            _movingAverage = currentValue;
            _previousValue = currentValue;
            _initialized = true;
            return CreateResult(TrendDirection.Stable);
        }
        
        // Update exponential moving average
        _movingAverage = (long)(_movingAverage * (1 - _alpha) + currentValue * _alpha);
        
        // Calculate change from previous value
        var diff = currentValue - _previousValue;
        var threshold = Math.Max((long)(_movingAverage * _thresholdPercent), _minimumThreshold);
        
        // Determine trend direction
        TrendDirection direction;
        if (currentValue == 0)
        {
            direction = TrendDirection.Idle;
        }
        else if (diff > threshold)
        {
            direction = TrendDirection.Rising;
        }
        else if (diff < -threshold)
        {
            direction = TrendDirection.Falling;
        }
        else
        {
            direction = TrendDirection.Stable;
        }
        
        // Store for next comparison
        _previousValue = currentValue;
        
        return CreateResult(direction);
    }
    
    /// <summary>
    /// Resets the calculator state.
    /// </summary>
    public void Reset()
    {
        _movingAverage = 0;
        _previousValue = 0;
        _initialized = false;
    }
    
    /// <summary>
    /// Gets the current moving average.
    /// </summary>
    public long MovingAverage => _movingAverage;
    
    private TrendResult CreateResult(TrendDirection direction)
    {
        var (icon, text) = direction switch
        {
            TrendDirection.Idle => _iconStyle == TrendIconStyle.Geometric 
                ? ("○", "idle") 
                : ("○", "idle"),
            TrendDirection.Rising => _iconStyle == TrendIconStyle.Geometric 
                ? ("▲", "rising") 
                : ("↑", "increasing"),
            TrendDirection.Falling => _iconStyle == TrendIconStyle.Geometric 
                ? ("▼", "falling") 
                : ("↓", "decreasing"),
            TrendDirection.Stable => _iconStyle == TrendIconStyle.Geometric 
                ? ("●", "stable") 
                : ("→", "stable"),
            _ => ("●", "stable")
        };
        
        return new TrendResult(direction, icon, text);
    }
}
