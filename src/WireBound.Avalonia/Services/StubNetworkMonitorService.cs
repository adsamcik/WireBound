using WireBound.Core.Models;
using WireBound.Core.Services;

namespace WireBound.Avalonia.Services;

/// <summary>
/// Stub network monitor service for non-Windows platforms.
/// Returns placeholder data until Linux/macOS implementations are available.
/// </summary>
public sealed class StubNetworkMonitorService : INetworkMonitorService
{
    private string _selectedAdapterId = "";
    private NetworkStats _currentStats = new();

    public event EventHandler<NetworkStats>? StatsUpdated;

    public bool IsUsingIpHelperApi => false;

    public IReadOnlyList<NetworkAdapter> GetAdapters(bool includeVirtual = false)
    {
        return new List<NetworkAdapter>
        {
            new()
            {
                Id = "stub-adapter",
                Name = "Stub Network Adapter",
                DisplayName = "Stub Network Adapter",
                Description = "Placeholder adapter for unsupported platforms",
                AdapterType = NetworkAdapterType.Unknown,
                IsActive = true,
                IsVirtual = false,
                IsKnownVpn = false,
                Category = "Physical"
            }
        };
    }

    public NetworkStats GetCurrentStats()
    {
        return _currentStats;
    }

    public NetworkStats GetStats(string adapterId)
    {
        return _currentStats;
    }

    public void SetAdapter(string adapterId)
    {
        _selectedAdapterId = adapterId;
    }

    public void SetUseIpHelperApi(bool useIpHelper)
    {
        // No-op on non-Windows platforms
    }

    public void Poll()
    {
        // Generate some fake data for testing
        var random = new Random();
        _currentStats = new NetworkStats
        {
            AdapterId = _selectedAdapterId.Length > 0 ? _selectedAdapterId : "stub-adapter",
            Timestamp = DateTime.Now,
            DownloadSpeedBps = (long)(random.NextDouble() * 1024 * 1024),
            UploadSpeedBps = (long)(random.NextDouble() * 256 * 1024),
            SessionBytesReceived = _currentStats.SessionBytesReceived + (long)(random.NextDouble() * 1024),
            SessionBytesSent = _currentStats.SessionBytesSent + (long)(random.NextDouble() * 256)
        };

        StatsUpdated?.Invoke(this, _currentStats);
    }

    public void ResetSession()
    {
        _currentStats = new NetworkStats
        {
            AdapterId = _selectedAdapterId.Length > 0 ? _selectedAdapterId : "stub-adapter",
            Timestamp = DateTime.Now
        };
    }
}
