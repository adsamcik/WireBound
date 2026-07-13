using WireBound.Platform.Abstract.Models;
using WireBound.Platform.Abstract.Services;

namespace WireBound.Platform.Stub.Services;

/// <summary>
/// Stub implementation of disk info provider for unsupported platforms and tests.
/// Generates realistic-looking random disk activity for development.
/// </summary>
public sealed class StubDiskInfoProvider : IDiskInfoProvider
{
    private readonly Random _random = new();

    public DiskInfoData GetDiskInfo()
    {
        // 0-50 MB/s read, 0-25 MB/s write, with correlated busy time.
        var readBytes = (long)(_random.NextDouble() * 50 * 1024 * 1024);
        var writeBytes = (long)(_random.NextDouble() * 25 * 1024 * 1024);
        var activity = Math.Clamp(_random.NextDouble() * 40 + 5, 0, 100);

        return new DiskInfoData
        {
            ReadBytesPerSecond = readBytes,
            WriteBytesPerSecond = writeBytes,
            ActivityPercent = activity
        };
    }

    public bool SupportsActivityPercent => true;
}
