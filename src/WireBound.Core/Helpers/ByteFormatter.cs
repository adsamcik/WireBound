using WireBound.Core.Models;

namespace WireBound.Core.Helpers;

/// <summary>
/// Utility class for formatting bytes and network speeds into human-readable strings.
/// Thread-safe for concurrent access.
/// </summary>
/// <remarks>
/// <para>
/// <strong>Design Decision: Global State for User Preferences</strong>
/// </para>
/// <para>
/// This class uses a static <see cref="UseSpeedInBits"/> property to store the user's
/// preferred speed display format. This is an intentional design choice that provides:
/// </para>
/// <list type="bullet">
/// <item><description>Consistent formatting across all UI elements without parameter passing</description></item>
/// <item><description>Simple integration - UI code calls <see cref="FormatSpeed(long)"/> and gets user-preferred format</description></item>
/// <item><description>Setting is managed centrally by SettingsViewModel at startup and when user changes preference</description></item>
/// </list>
/// <para>
/// <strong>Testing Strategy</strong>
/// </para>
/// <para>
/// For testing purposes, use the <see cref="FormatSpeed(long, SpeedUnit)"/> overload which accepts
/// an explicit <see cref="SpeedUnit"/> parameter and ignores the global setting. Tests should also
/// reset <see cref="UseSpeedInBits"/> to a known state in their setup/constructor to ensure isolation.
/// </para>
/// </remarks>
public static class ByteFormatter
{
    // Use volatile to ensure thread-safe reads/writes of the boolean flag
    private static volatile bool _useSpeedInBits;

    /// <summary>
    /// Gets or sets the global speed display mode. When true, displays in bits (Mbps). When false, displays in bytes (MB/s).
    /// </summary>
    /// <remarks>
    /// This property is thread-safe for individual reads/writes. It is set by SettingsViewModel
    /// when the application starts and when the user changes their preference. Use the
    /// <see cref="FormatSpeed(long, SpeedUnit)"/> overload in tests to avoid dependency on global state.
    /// </remarks>
    public static bool UseSpeedInBits
    {
        get => _useSpeedInBits;
        set => _useSpeedInBits = value;
    }

    /// <summary>
    /// Formats bytes per second into a human-readable speed string using the global <see cref="UseSpeedInBits"/> setting.
    /// </summary>
    /// <param name="bytesPerSecond">Speed in bytes per second</param>
    /// <returns>Formatted string like "1.50 MB/s" or "12.00 Mbps" depending on user preference</returns>
    /// <remarks>
    /// Use this overload for UI display where you want consistent formatting based on user preferences.
    /// For tests or when explicit control is needed, use <see cref="FormatSpeed(long, SpeedUnit)"/> instead.
    /// </remarks>
    public static string FormatSpeed(long bytesPerSecond)
    {
        return UseSpeedInBits ? FormatSpeedInBits(bytesPerSecond) : FormatSpeedInBytes(bytesPerSecond);
    }

    /// <summary>
    /// Formats bytes per second into a human-readable speed string using the specified unit.
    /// </summary>
    /// <param name="bytesPerSecond">Speed in bytes per second</param>
    /// <param name="unit">The speed unit to use for formatting (ignored global setting)</param>
    /// <returns>Formatted string like "1.50 MB/s" or "12.00 Mbps"</returns>
    /// <remarks>
    /// Use this overload for explicit control over the output format, especially in tests.
    /// This method ignores the global <see cref="UseSpeedInBits"/> setting.
    /// </remarks>
    public static string FormatSpeed(long bytesPerSecond, SpeedUnit unit)
    {
        return unit == SpeedUnit.BitsPerSecond
            ? FormatSpeedInBits(bytesPerSecond)
            : FormatSpeedInBytes(bytesPerSecond);
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
