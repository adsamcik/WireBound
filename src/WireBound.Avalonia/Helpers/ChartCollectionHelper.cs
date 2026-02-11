using LiveChartsCore.Defaults;
using System.Collections.ObjectModel;

namespace WireBound.Avalonia.Helpers;

/// <summary>
/// Helper methods for efficiently trimming ObservableCollection data used in charts.
/// </summary>
public static class ChartCollectionHelper
{
    /// <summary>
    /// Efficiently removes excess items from the beginning of the collection, keeping only the
    /// most recent <paramref name="maxCount"/> items. Uses batch clear-and-re-add which is O(n)
    /// compared to O(n²) for repeated RemoveAt(0) calls, and triggers fewer UI updates.
    /// </summary>
    public static void TrimToMaxCount(ObservableCollection<DateTimePoint> points, int maxCount)
    {
        var removeCount = points.Count - maxCount;
        if (removeCount <= 0)
            return;

        var keepCount = points.Count - removeCount;
        var pointsToKeep = new DateTimePoint[keepCount];
        for (var i = 0; i < keepCount; i++)
            pointsToKeep[i] = points[removeCount + i];

        points.Clear();
        foreach (var point in pointsToKeep)
            points.Add(point);
    }

    /// <summary>
    /// Efficiently removes all points with a timestamp before the cutoff time.
    /// Uses batch clear-and-re-add which is O(n) compared to O(n²) for repeated RemoveAt(0) calls.
    /// </summary>
    public static void TrimBeforeCutoff(ObservableCollection<DateTimePoint> points, DateTime cutoff)
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

        var pointsToKeep = new DateTimePoint[points.Count - firstValidIndex];
        for (var i = firstValidIndex; i < points.Count; i++)
            pointsToKeep[i - firstValidIndex] = points[i];

        points.Clear();
        foreach (var point in pointsToKeep)
            points.Add(point);
    }
}
