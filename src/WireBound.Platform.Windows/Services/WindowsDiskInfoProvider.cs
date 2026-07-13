using System.Diagnostics;
using System.Runtime.Versioning;
using WireBound.Platform.Abstract.Models;
using WireBound.Platform.Abstract.Services;

namespace WireBound.Platform.Windows.Services;

/// <summary>
/// Windows implementation of disk info provider using PhysicalDisk performance counters.
/// Reads aggregate throughput and busy time across all physical disks (_Total instance).
/// </summary>
[SupportedOSPlatform("windows")]
public sealed class WindowsDiskInfoProvider : IDiskInfoProvider, IDisposable
{
    private readonly PerformanceCounter? _readBytesCounter;
    private readonly PerformanceCounter? _writeBytesCounter;
    private readonly PerformanceCounter? _diskTimeCounter;
    private readonly bool _available;
    private bool _disposed;

    public WindowsDiskInfoProvider()
    {
        try
        {
            _readBytesCounter = new PerformanceCounter("PhysicalDisk", "Disk Read Bytes/sec", "_Total", true);
            _writeBytesCounter = new PerformanceCounter("PhysicalDisk", "Disk Write Bytes/sec", "_Total", true);
            _diskTimeCounter = new PerformanceCounter("PhysicalDisk", "% Disk Time", "_Total", true);

            // First call to NextValue() returns 0, prime the counters.
            _ = _readBytesCounter.NextValue();
            _ = _writeBytesCounter.NextValue();
            _ = _diskTimeCounter.NextValue();
            _available = true;
        }
        catch
        {
            // Performance counters may be unavailable (corrupt registry, disabled service).
            _available = false;
        }
    }

    public DiskInfoData GetDiskInfo()
    {
        ObjectDisposedException.ThrowIf(_disposed, this);

        if (!_available || _readBytesCounter is null || _writeBytesCounter is null || _diskTimeCounter is null)
        {
            return new DiskInfoData();
        }

        try
        {
            var readBytes = _readBytesCounter.NextValue();
            var writeBytes = _writeBytesCounter.NextValue();
            // "% Disk Time" can exceed 100 on multi-disk systems; clamp to a sane range.
            var activity = Math.Clamp(_diskTimeCounter.NextValue(), 0, 100);

            return new DiskInfoData
            {
                ReadBytesPerSecond = (long)Math.Max(0, readBytes),
                WriteBytesPerSecond = (long)Math.Max(0, writeBytes),
                ActivityPercent = activity
            };
        }
        catch
        {
            return new DiskInfoData();
        }
    }

    public bool SupportsActivityPercent => _available;

    public void Dispose()
    {
        if (_disposed) return;

        _readBytesCounter?.Dispose();
        _writeBytesCounter?.Dispose();
        _diskTimeCounter?.Dispose();

        _disposed = true;
    }
}
