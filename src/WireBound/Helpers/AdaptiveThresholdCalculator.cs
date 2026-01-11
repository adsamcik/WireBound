namespace WireBound.Helpers;

/// <summary>
/// Helper class for calculating adaptive thresholds for network speed charts.
/// Similar to Windows Task Manager's dynamic scaling behavior.
/// </summary>
public class AdaptiveThresholdCalculator
{
    private readonly Queue<double> _rollingMaxBuffer = new();
    private readonly int _windowSize;
    private readonly double _smoothingFactor;
    private double _smoothedThreshold;

    /// <summary>
    /// Creates a new adaptive threshold calculator.
    /// </summary>
    /// <param name="windowSize">Number of samples to consider for rolling max (default 60 = 1 minute at 1Hz)</param>
    /// <param name="smoothingFactor">EMA smoothing factor, lower = smoother (default 0.1)</param>
    public AdaptiveThresholdCalculator(int windowSize = 60, double smoothingFactor = 0.1)
    {
        _windowSize = windowSize;
        _smoothingFactor = smoothingFactor;
        _smoothedThreshold = 0;
    }

    /// <summary>
    /// Updates the threshold based on a new data value.
    /// </summary>
    /// <param name="value">The new data value (bytes per second)</param>
    /// <returns>The current smoothed threshold</returns>
    public double Update(double value)
    {
        // Add to rolling buffer
        _rollingMaxBuffer.Enqueue(value);
        
        // Trim buffer to window size
        while (_rollingMaxBuffer.Count > _windowSize)
        {
            _rollingMaxBuffer.Dequeue();
        }

        // Calculate rolling max
        double rollingMax = _rollingMaxBuffer.Count > 0 ? _rollingMaxBuffer.Max() : 0;

        // Apply exponential moving average smoothing
        // This prevents erratic jumps when peaks occur
        if (_smoothedThreshold == 0)
        {
            _smoothedThreshold = rollingMax;
        }
        else
        {
            // Only allow threshold to increase quickly, but decrease slowly
            if (rollingMax > _smoothedThreshold)
            {
                // Quick response to increases (factor of 0.3)
                _smoothedThreshold = _smoothedThreshold * 0.7 + rollingMax * 0.3;
            }
            else
            {
                // Slow decay for decreases (factor from smoothingFactor)
                _smoothedThreshold = _smoothedThreshold * (1 - _smoothingFactor) + rollingMax * _smoothingFactor;
            }
        }

        // Round to a nice human-readable value
        return RoundToNiceValue(_smoothedThreshold);
    }

    /// <summary>
    /// Gets the current threshold levels as percentages of the max.
    /// Returns thresholds at 25%, 50%, 75%, and 100%.
    /// </summary>
    public (double Quarter, double Half, double ThreeQuarter, double Full) GetThresholdLevels()
    {
        var niceMax = RoundToNiceValue(_smoothedThreshold);
        return (niceMax * 0.25, niceMax * 0.5, niceMax * 0.75, niceMax);
    }

    /// <summary>
    /// Rounds a byte value to a nice human-readable number.
    /// Examples: 1 KB/s, 10 KB/s, 100 KB/s, 1 MB/s, 10 MB/s, etc.
    /// </summary>
    public static double RoundToNiceValue(double bytesPerSecond)
    {
        if (bytesPerSecond <= 0) return 1024; // Minimum 1 KB/s

        // Define nice value boundaries
        double[] niceValues = [
            512,           // 512 B/s
            1024,          // 1 KB/s
            5 * 1024,      // 5 KB/s
            10 * 1024,     // 10 KB/s
            50 * 1024,     // 50 KB/s
            100 * 1024,    // 100 KB/s
            500 * 1024,    // 500 KB/s
            1024 * 1024,   // 1 MB/s
            5 * 1024 * 1024,   // 5 MB/s
            10 * 1024 * 1024,  // 10 MB/s
            50 * 1024 * 1024,  // 50 MB/s
            100 * 1024 * 1024, // 100 MB/s
            500 * 1024 * 1024, // 500 MB/s
            1024L * 1024 * 1024,    // 1 GB/s
            10L * 1024 * 1024 * 1024 // 10 GB/s
        ];

        // Find the smallest nice value that's >= the input
        foreach (var nice in niceValues)
        {
            if (bytesPerSecond <= nice)
                return nice;
        }

        return niceValues[^1];
    }

    /// <summary>
    /// Resets the calculator state.
    /// </summary>
    public void Reset()
    {
        _rollingMaxBuffer.Clear();
        _smoothedThreshold = 0;
    }
}
