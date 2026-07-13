using System.Diagnostics;
using System.Globalization;
using System.Runtime.Versioning;
using WireBound.Platform.Abstract.Models;
using WireBound.Platform.Abstract.Services;

namespace WireBound.Platform.Linux.Services;

/// <summary>
/// Linux implementation of disk info provider using the /proc/diskstats filesystem.
/// Aggregates read/write throughput across physical disks and derives a busy percentage
/// from the io_ticks field (time spent doing I/O).
/// </summary>
[SupportedOSPlatform("linux")]
public sealed class LinuxDiskInfoProvider : IDiskInfoProvider
{
    private const string DiskStatsPath = "/proc/diskstats";

    /// <summary>
    /// Linux reports disk I/O in 512-byte sectors regardless of physical sector size.
    /// </summary>
    private const long SectorSizeBytes = 512;

    private readonly Dictionary<string, DiskCounters> _previous = new();
    private readonly Stopwatch _stopwatch = new();
    private bool _hasBaseline;

    private readonly record struct DiskCounters(long ReadSectors, long WriteSectors, long IoTicksMs);

    public LinuxDiskInfoProvider()
    {
        _stopwatch.Start();
    }

    public DiskInfoData GetDiskInfo()
    {
        if (!File.Exists(DiskStatsPath))
        {
            return new DiskInfoData();
        }

        var elapsedSeconds = _stopwatch.Elapsed.TotalSeconds;
        var elapsedMs = _stopwatch.Elapsed.TotalMilliseconds;
        _stopwatch.Restart();

        try
        {
            var lines = File.ReadAllLines(DiskStatsPath);

            long readSectorsDelta = 0;
            long writeSectorsDelta = 0;
            double maxBusyPercent = 0;
            var current = new Dictionary<string, DiskCounters>(lines.Length);

            foreach (var line in lines)
            {
                var parts = line.Split(' ', StringSplitOptions.RemoveEmptyEntries);
                if (parts.Length < 14)
                    continue;

                var name = parts[2];
                if (!IsPhysicalDisk(name))
                    continue;

                if (!long.TryParse(parts[5], NumberStyles.Integer, CultureInfo.InvariantCulture, out var readSectors) ||
                    !long.TryParse(parts[9], NumberStyles.Integer, CultureInfo.InvariantCulture, out var writeSectors) ||
                    !long.TryParse(parts[12], NumberStyles.Integer, CultureInfo.InvariantCulture, out var ioTicks))
                {
                    continue;
                }

                var counters = new DiskCounters(readSectors, writeSectors, ioTicks);
                current[name] = counters;

                if (_hasBaseline && _previous.TryGetValue(name, out var prev))
                {
                    // Counters monotonically increase; guard against wrap/reset with Max(0, ...).
                    readSectorsDelta += Math.Max(0, readSectors - prev.ReadSectors);
                    writeSectorsDelta += Math.Max(0, writeSectors - prev.WriteSectors);

                    if (elapsedMs > 0)
                    {
                        var busyMs = Math.Max(0, ioTicks - prev.IoTicksMs);
                        var busyPercent = busyMs / elapsedMs * 100.0;
                        if (busyPercent > maxBusyPercent)
                            maxBusyPercent = busyPercent;
                    }
                }
            }

            _previous.Clear();
            foreach (var kvp in current)
                _previous[kvp.Key] = kvp.Value;
            _hasBaseline = true;

            if (elapsedSeconds <= 0)
                return new DiskInfoData();

            return new DiskInfoData
            {
                ReadBytesPerSecond = (long)(readSectorsDelta * SectorSizeBytes / elapsedSeconds),
                WriteBytesPerSecond = (long)(writeSectorsDelta * SectorSizeBytes / elapsedSeconds),
                ActivityPercent = Math.Clamp(maxBusyPercent, 0, 100)
            };
        }
        catch
        {
            return new DiskInfoData();
        }
    }

    /// <summary>
    /// Determines whether a /proc/diskstats device name represents a whole physical disk,
    /// excluding partitions and virtual devices to avoid double counting.
    /// </summary>
    internal static bool IsPhysicalDisk(string name)
    {
        if (string.IsNullOrEmpty(name))
            return false;

        // Skip virtual / pseudo devices.
        if (name.StartsWith("loop", StringComparison.Ordinal) ||
            name.StartsWith("ram", StringComparison.Ordinal) ||
            name.StartsWith("zram", StringComparison.Ordinal) ||
            name.StartsWith("dm-", StringComparison.Ordinal) ||
            name.StartsWith("md", StringComparison.Ordinal) ||
            name.StartsWith("sr", StringComparison.Ordinal) ||
            name.StartsWith("fd", StringComparison.Ordinal))
        {
            return false;
        }

        // SCSI/SATA/virtio/IDE whole disks: sda, vdb, hdc (letters only, no trailing digit = partition).
        if ((name.StartsWith("sd", StringComparison.Ordinal) ||
             name.StartsWith("vd", StringComparison.Ordinal) ||
             name.StartsWith("hd", StringComparison.Ordinal)) &&
            !char.IsDigit(name[^1]))
        {
            return true;
        }

        // NVMe and eMMC whole disks: nvme0n1, mmcblk0. Their partitions contain "p<digits>".
        if (name.StartsWith("nvme", StringComparison.Ordinal) ||
            name.StartsWith("mmcblk", StringComparison.Ordinal))
        {
            return !IsNvmeStylePartition(name);
        }

        return false;
    }

    private static bool IsNvmeStylePartition(string name)
    {
        // Partition form ends with 'p' followed by digits, e.g. nvme0n1p2 or mmcblk0p1.
        var pIndex = name.LastIndexOf('p');
        if (pIndex <= 0 || pIndex == name.Length - 1)
            return false;

        for (var i = pIndex + 1; i < name.Length; i++)
        {
            if (!char.IsDigit(name[i]))
                return false;
        }

        return true;
    }

    public bool SupportsActivityPercent => File.Exists(DiskStatsPath);
}
