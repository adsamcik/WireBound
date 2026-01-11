using LiveChartsCore.Defaults;

namespace WireBound.Helpers;

/// <summary>
/// Largest-Triangle-Three-Buckets (LTTB) downsampling algorithm.
/// Reduces data points while preserving visual peaks and valleys.
/// </summary>
public static class LttbDownsampler
{
    /// <summary>
    /// Downsample a list of DateTimePoints using the LTTB algorithm.
    /// </summary>
    /// <param name="data">The source data points</param>
    /// <param name="targetPoints">The desired number of output points</param>
    /// <returns>Downsampled list preserving visual features</returns>
    public static List<DateTimePoint> Downsample(IReadOnlyList<DateTimePoint> data, int targetPoints)
    {
        if (data == null || data.Count == 0)
            return [];

        if (data.Count <= targetPoints || targetPoints < 3)
            return [.. data];

        var result = new List<DateTimePoint>(targetPoints);

        // Always keep first point
        result.Add(data[0]);

        // Calculate bucket size (excluding first and last points)
        double bucketSize = (double)(data.Count - 2) / (targetPoints - 2);

        int previousSelectedIndex = 0;

        for (int i = 0; i < targetPoints - 2; i++)
        {
            // Calculate bucket boundaries
            int bucketStart = (int)(i * bucketSize) + 1;
            int bucketEnd = (int)((i + 1) * bucketSize) + 1;
            bucketEnd = Math.Min(bucketEnd, data.Count - 1);

            // Calculate average point in next bucket for triangle calculation
            int nextBucketStart = bucketEnd;
            int nextBucketEnd = (int)((i + 2) * bucketSize) + 1;
            nextBucketEnd = Math.Min(nextBucketEnd, data.Count);

            double avgX = 0, avgY = 0;
            int nextBucketCount = nextBucketEnd - nextBucketStart;
            
            if (nextBucketCount > 0)
            {
                for (int j = nextBucketStart; j < nextBucketEnd; j++)
                {
                    avgX += data[j].DateTime.Ticks;
                    avgY += data[j].Value ?? 0;
                }
                avgX /= nextBucketCount;
                avgY /= nextBucketCount;
            }

            // Find point in current bucket with largest triangle area
            double maxArea = -1;
            int maxAreaIndex = bucketStart;

            var prevPoint = data[previousSelectedIndex];
            double prevX = prevPoint.DateTime.Ticks;
            double prevY = prevPoint.Value ?? 0;

            for (int j = bucketStart; j < bucketEnd; j++)
            {
                var point = data[j];
                double x = point.DateTime.Ticks;
                double y = point.Value ?? 0;

                // Calculate triangle area using cross product
                double area = Math.Abs(
                    (prevX - avgX) * (y - prevY) -
                    (prevX - x) * (avgY - prevY)
                ) / 2.0;

                if (area > maxArea)
                {
                    maxArea = area;
                    maxAreaIndex = j;
                }
            }

            result.Add(data[maxAreaIndex]);
            previousSelectedIndex = maxAreaIndex;
        }

        // Always keep last point
        result.Add(data[^1]);

        return result;
    }
}
