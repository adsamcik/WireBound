using Microsoft.Extensions.Logging;
using WireBound.Core.Services;
using WireBound.Platform.Abstract.Services;

namespace WireBound.Avalonia.Services;

/// <summary>
/// WiFi info service that delegates to platform-specific provider via DI.
/// </summary>
public sealed class WiFiInfoService : IWiFiInfoService
{
    private readonly ILogger<WiFiInfoService> _logger;
    private readonly IWiFiInfoProvider _provider;

    public WiFiInfoService(ILogger<WiFiInfoService> logger, IWiFiInfoProvider provider)
    {
        _logger = logger;
        _provider = provider;
    }

    public bool IsSupported => _provider.IsSupported;

    public WiFiInfo? GetWiFiInfo(string adapterId)
    {
        try
        {
            return _provider.GetWiFiInfo(adapterId);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to get WiFi info for adapter {AdapterId}", adapterId);
            return null;
        }
    }

    public Dictionary<string, WiFiInfo> GetAllWiFiInfo()
    {
        try
        {
            return _provider.GetAllWiFiInfo();
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to get all WiFi info");
            return [];
        }
    }
}
