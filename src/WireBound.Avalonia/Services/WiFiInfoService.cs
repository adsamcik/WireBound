using System.Runtime.InteropServices;
using Microsoft.Extensions.Logging;
using WireBound.Core.Models;
using WireBound.Core.Services;

namespace WireBound.Avalonia.Services;

/// <summary>
/// Cross-platform WiFi info service with platform-specific implementations
/// </summary>
public class WiFiInfoService : IWiFiInfoService
{
    private readonly ILogger<WiFiInfoService> _logger;
    private readonly IWiFiInfoProvider _provider;
    
    public WiFiInfoService(ILogger<WiFiInfoService> logger)
    {
        _logger = logger;
        _provider = CreatePlatformProvider();
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
            return new Dictionary<string, WiFiInfo>();
        }
    }
    
    private IWiFiInfoProvider CreatePlatformProvider()
    {
        if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
        {
            return new WindowsWiFiInfoProvider(_logger);
        }
        else if (RuntimeInformation.IsOSPlatform(OSPlatform.Linux))
        {
            return new LinuxWiFiInfoProvider(_logger);
        }
        else if (RuntimeInformation.IsOSPlatform(OSPlatform.OSX))
        {
            return new MacOsWiFiInfoProvider(_logger);
        }
        else
        {
            return new NullWiFiInfoProvider();
        }
    }
}

/// <summary>
/// Internal interface for platform-specific providers
/// </summary>
internal interface IWiFiInfoProvider
{
    bool IsSupported { get; }
    WiFiInfo? GetWiFiInfo(string adapterId);
    Dictionary<string, WiFiInfo> GetAllWiFiInfo();
}

/// <summary>
/// Fallback provider for unsupported platforms
/// </summary>
internal class NullWiFiInfoProvider : IWiFiInfoProvider
{
    public bool IsSupported => false;
    public WiFiInfo? GetWiFiInfo(string adapterId) => null;
    public Dictionary<string, WiFiInfo> GetAllWiFiInfo() => new();
}
