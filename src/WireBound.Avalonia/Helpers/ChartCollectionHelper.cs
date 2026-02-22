using LiveChartsCore.Defaults;
using System.Collections.ObjectModel;

namespace WireBound.Avalonia.Helpers;

/// <summary>
/// Helper methods for efficiently trimming ObservableCollection data used in charts.
/// Uses <see cref="BatchObservableCollection{T}.ReplaceAll"/> to fire a single Reset
/// notification instead of per-item Add notifications.
/// </summary>
public static class ChartCollectionHelper
{
    /// <summary>
    /// Efficiently removes excess items from the beginning of the collection, keeping only the
    /// most recent <paramref name="maxCount"/> items. Fires a single collection change notification.
    /// </summary>
    public static void TrimToMaxCount(BatchObservableCollection<DateTimePoint> points, int maxCount)
    {
        var removeCount = points.Count - maxCount;
        if (removeCount <= 0)
            return;

        var keepCount = points.Count - removeCount;
        var pointsToKeep = new DateTimePoint[keepCount];
        for (var i = 0; i < keepCount; i++)
            pointsToKeep[i] = points[removeCount + i];

        points.ReplaceAll(pointsToKeep);
    }

    /// <summary>
    /// Efficiently removes all points with a timestamp before the cutoff time.
    /// Optimized for the common case of removing a single point per tick (zero allocation).
    /// Falls back to batch replacement for larger trims.
    /// </summary>
    public static void TrimBeforeCutoff(BatchObservableCollection<DateTimePoint> points, DateTime cutoff)
    {
        if (points.Count == 0 || points[0].DateTime >= cutoff)
            return;

        // Find the first index where DateTime >= cutoff
        var firstValidIndex = 0;
        for (var i = 0; i < points.Count; i++)
        {
            if (points[i].DateTime >= cutoff)
            {
                firstValidIndex = i;
                break;
            }
            if (i == points.Count - 1)
            {
                points.Clear();
                return;
            }
        }

        if (firstValidIndex == 0)
            return;

        // Common case: removing 1 point per tick — use RemoveAt to avoid array allocation
        if (firstValidIndex == 1)
        {
            points.RemoveAt(0);
            return;
        }

        // Larger trim: batch replace is more efficient than multiple RemoveAt(0) calls
        var pointsToKeep = new DateTimePoint[points.Count - firstValidIndex];
        for (var i = firstValidIndex; i < points.Count; i++)
            pointsToKeep[i - firstValidIndex] = points[i];

        points.ReplaceAll(pointsToKeep);
    }
}
