using WireBound.Platform.Abstract.Services;

namespace WireBound.Platform.Stub.Services;

/// <summary>
/// Stub implementation for platforms that don't support WiFi info retrieval.
/// </summary>
public sealed class StubWiFiInfoProvider : IWiFiInfoProvider
{
    public bool IsSupported => false;

    public WiFiInfo? GetWiFiInfo(string adapterId) => null;

    public Dictionary<string, WiFiInfo> GetAllWiFiInfo() => [];
}
