namespace WireBound.Core.Helpers;

/// <summary>
/// Utility class for formatting bytes and network speeds into human-readable strings
/// </summary>
public static class ByteFormatter
{
    /// <summary>
    /// Formats bytes per second into a human-readable speed string
    /// </summary>
    /// <param name="bytesPerSecond">Speed in bytes per second</param>
    /// <returns>Formatted string like "1.50 MB/s"</returns>
    public static string FormatSpeed(long bytesPerSecond)
    {
        return bytesPerSecond switch
        {
            >= 1_073_741_824 => $"{bytesPerSecond / 1_073_741_824.0:F2} GB/s",
            >= 1_048_576 => $"{bytesPerSecond / 1_048_576.0:F2} MB/s",
            >= 1024 => $"{bytesPerSecond / 1024.0:F2} KB/s",
            _ => $"{bytesPerSecond} B/s"
        };
    }

    /// <summary>
    /// Formats bytes into a human-readable size string
    /// </summary>
    /// <param name="bytes">Size in bytes</param>
    /// <returns>Formatted string like "1.50 GB"</returns>
    public static string FormatBytes(long bytes)
    {
        return bytes switch
        {
            >= 1_099_511_627_776 => $"{bytes / 1_099_511_627_776.0:F2} TB",
            >= 1_073_741_824 => $"{bytes / 1_073_741_824.0:F2} GB",
            >= 1_048_576 => $"{bytes / 1_048_576.0:F2} MB",
            >= 1024 => $"{bytes / 1024.0:F2} KB",
            _ => $"{bytes} B"
        };
    }
}
