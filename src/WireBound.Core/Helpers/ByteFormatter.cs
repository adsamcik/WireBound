namespace WireBound.Core.Helpers;

/// <summary>
/// Utility class for formatting bytes and network speeds into human-readable strings
/// </summary>
public static class ByteFormatter
{
    /// <summary>
    /// Current speed display mode. When true, displays in bits (Mbps). When false, displays in bytes (MB/s).
    /// </summary>
    public static bool UseSpeedInBits { get; set; } = false;

    /// <summary>
    /// Formats bytes per second into a human-readable speed string.
    /// Uses the current UseSpeedInBits setting to determine the unit.
    /// </summary>
    /// <param name="bytesPerSecond">Speed in bytes per second</param>
    /// <returns>Formatted string like "1.50 MB/s" or "12.00 Mbps"</returns>
    public static string FormatSpeed(long bytesPerSecond)
    {
        return UseSpeedInBits ? FormatSpeedInBits(bytesPerSecond) : FormatSpeedInBytes(bytesPerSecond);
    }

    /// <summary>
    /// Formats bytes per second into bytes-based speed string (KB/s, MB/s, GB/s)
    /// </summary>
    public static string FormatSpeedInBytes(long bytesPerSecond)
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
    /// Formats bytes per second into bits-based speed string (Kbps, Mbps, Gbps)
    /// </summary>
    public static string FormatSpeedInBits(long bytesPerSecond)
    {
        var bitsPerSecond = bytesPerSecond * 8;
        return bitsPerSecond switch
        {
            >= 1_000_000_000 => $"{bitsPerSecond / 1_000_000_000.0:F2} Gbps",
            >= 1_000_000 => $"{bitsPerSecond / 1_000_000.0:F2} Mbps",
            >= 1_000 => $"{bitsPerSecond / 1_000.0:F2} Kbps",
            _ => $"{bitsPerSecond} bps"
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
