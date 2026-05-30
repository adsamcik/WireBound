using System.Collections.Generic;

namespace WireBound.Core.Helpers;

/// <summary>
/// Thread-safe rolling time window that keeps numeric samples from the last
/// <c>window</c> duration and exposes their arithmetic mean. Samples older
/// than the window are evicted lazily on every read or write, so the buffer
/// never grows unbounded for sparse callers.
/// </summary>
/// <remarks>
/// Designed for low-frequency telemetry (one sample every few seconds), so a
/// linked-list backing store is fine — eviction is O(k) where k is the number
/// of expired samples per operation, typically 0 or 1. Used by
/// <see cref="WireBound.Core.Services.IResourceInsightsService"/> to surface
/// a rolling-60-seconds CPU percent per application without buying the full
/// cost of a per-process running stats library.
/// </remarks>
public sealed class TimeWindowedAverage
{
    private readonly LinkedList<(DateTime Timestamp, double Value)> _samples = new();
    private readonly object _lock = new();
    private readonly TimeSpan _window;

    public TimeWindowedAverage(TimeSpan window)
    {
        if (window <= TimeSpan.Zero)
        {
            throw new ArgumentOutOfRangeException(nameof(window), "Window must be positive.");
        }
        _window = window;
    }

    /// <summary>Append a sample taken at <paramref name="now"/>.</summary>
    public void Add(double value, DateTime now)
    {
        lock (_lock)
        {
            _samples.AddLast((now, value));
            Evict(now);
        }
    }

    /// <summary>
    /// Returns the mean of samples newer than <c>now - window</c>, or
    /// <c>double.NaN</c> when no samples remain in window. NaN intentionally
    /// signals "no data yet" so callers can render a clear placeholder
    /// instead of a misleading 0.0.
    /// </summary>
    public double GetAverage(DateTime now)
    {
        lock (_lock)
        {
            Evict(now);
            if (_samples.Count == 0)
            {
                return double.NaN;
            }

            double sum = 0;
            foreach (var sample in _samples)
            {
                sum += sample.Value;
            }
            return sum / _samples.Count;
        }
    }

    /// <summary>
    /// Returns the mean of samples newer than <c>now - subWindow</c>. Lets
    /// callers read a tighter slice than the buffer's retention without
    /// holding a second buffer. <paramref name="subWindow"/> larger than the
    /// underlying window is silently clamped — you only get back what's
    /// already retained.
    /// </summary>
    public double GetAverage(TimeSpan subWindow, DateTime now)
    {
        if (subWindow <= TimeSpan.Zero)
        {
            throw new ArgumentOutOfRangeException(nameof(subWindow), "Sub-window must be positive.");
        }

        lock (_lock)
        {
            Evict(now);
            if (_samples.Count == 0)
            {
                return double.NaN;
            }

            var cutoff = now - subWindow;
            double sum = 0;
            int count = 0;
            foreach (var sample in _samples)
            {
                if (sample.Timestamp >= cutoff)
                {
                    sum += sample.Value;
                    count++;
                }
            }
            return count == 0 ? double.NaN : sum / count;
        }
    }

    /// <summary>Returns how many samples currently live in the window.</summary>
    public int Count
    {
        get
        {
            lock (_lock)
            {
                return _samples.Count;
            }
        }
    }

    private void Evict(DateTime now)
    {
        var cutoff = now - _window;
        while (_samples.First is { } first && first.Value.Timestamp < cutoff)
        {
            _samples.RemoveFirst();
        }
    }
}
