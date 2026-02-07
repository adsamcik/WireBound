using Microsoft.Extensions.Logging;
using WireBound.Avalonia.Services;
using WireBound.Platform.Abstract.Services;

namespace WireBound.Tests.Services;

/// <summary>
/// Unit tests for WiFiInfoService
/// </summary>
public class WiFiInfoServiceTests
{
    private readonly IWiFiInfoProvider _provider;
    private readonly ILogger<WiFiInfoService> _logger;
    private readonly WiFiInfoService _service;

    public WiFiInfoServiceTests()
    {
        _provider = Substitute.For<IWiFiInfoProvider>();
        _logger = Substitute.For<ILogger<WiFiInfoService>>();
        _service = new WiFiInfoService(_logger, _provider);
    }

    // ═══════════════════════════════════════════════════════════════════════
    // IsSupported Tests
    // ═══════════════════════════════════════════════════════════════════════

    [Test]
    public void IsSupported_DelegatesToProvider()
    {
        // Arrange
        _provider.IsSupported.Returns(true);

        // Act
        var result = _service.IsSupported;

        // Assert
        result.Should().BeTrue();
    }

    [Test]
    public void IsSupported_WhenProviderReturnsFalse_ReturnsFalse()
    {
        // Arrange
        _provider.IsSupported.Returns(false);

        // Act
        var result = _service.IsSupported;

        // Assert
        result.Should().BeFalse();
    }

    // ═══════════════════════════════════════════════════════════════════════
    // GetWiFiInfo Tests
    // ═══════════════════════════════════════════════════════════════════════

    [Test]
    public void GetWiFiInfo_DelegatesToProvider()
    {
        // Arrange
        var wifiInfo = new WiFiInfo { Ssid = "TestNetwork", SignalStrength = 75 };
        _provider.GetWiFiInfo("adapter1").Returns(wifiInfo);

        // Act
        var result = _service.GetWiFiInfo("adapter1");

        // Assert
        result.Should().NotBeNull();
        result!.Ssid.Should().Be("TestNetwork");
        result.SignalStrength.Should().Be(75);
    }

    [Test]
    public void GetWiFiInfo_WhenProviderThrows_ReturnsNull()
    {
        // Arrange
        _provider.GetWiFiInfo("adapter1").Returns(_ => throw new InvalidOperationException("WiFi error"));

        // Act
        var result = _service.GetWiFiInfo("adapter1");

        // Assert
        result.Should().BeNull();
    }

    // ═══════════════════════════════════════════════════════════════════════
    // GetAllWiFiInfo Tests
    // ═══════════════════════════════════════════════════════════════════════

    [Test]
    public void GetAllWiFiInfo_DelegatesToProvider()
    {
        // Arrange
        var allInfo = new Dictionary<string, WiFiInfo>
        {
            ["adapter1"] = new WiFiInfo { Ssid = "Network1", SignalStrength = 80 },
            ["adapter2"] = new WiFiInfo { Ssid = "Network2", SignalStrength = 60 }
        };
        _provider.GetAllWiFiInfo().Returns(allInfo);

        // Act
        var result = _service.GetAllWiFiInfo();

        // Assert
        result.Should().HaveCount(2);
        result["adapter1"].Ssid.Should().Be("Network1");
        result["adapter2"].Ssid.Should().Be("Network2");
    }

    [Test]
    public void GetAllWiFiInfo_WhenProviderThrows_ReturnsEmptyDictionary()
    {
        // Arrange
        _provider.GetAllWiFiInfo().Returns(_ => throw new InvalidOperationException("WiFi error"));

        // Act
        var result = _service.GetAllWiFiInfo();

        // Assert
        result.Should().BeEmpty();
    }
}
