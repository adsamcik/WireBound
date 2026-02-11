#pragma warning disable CA1416

using Microsoft.Extensions.Logging;
using WireBound.Platform.Linux.Services;
using WireBound.Platform.Abstract.Services;

namespace WireBound.Tests.Platform;

public class LinuxWiFiInfoProviderTests
{
    private readonly LinuxWiFiInfoProvider _provider;

    public LinuxWiFiInfoProviderTests()
    {
        var logger = Substitute.For<ILogger<LinuxWiFiInfoProvider>>();
        _provider = new LinuxWiFiInfoProvider(logger);
    }

    // ÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉ
    // ParseNmcliOutput Tests
    // ÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉ

    [Test]
    public void ParseNmcliOutput_SingleActiveConnection_AddsEntry()
    {
        // Arrange
        var output = "wlan0:yes:MyNetwork:75:5180:36:WPA2";
        var result = new Dictionary<string, WiFiInfo>();

        // Act
        _provider.ParseNmcliOutput(output, result);

        // Assert
        result.Should().HaveCount(1);
        result.Should().ContainKey("wlan0");
        result["wlan0"].Ssid.Should().Be("MyNetwork");
        result["wlan0"].SignalStrength.Should().Be(75);
        result["wlan0"].FrequencyMhz.Should().Be(5180);
        result["wlan0"].Channel.Should().Be(36);
        result["wlan0"].Security.Should().Be("WPA2");
    }

    [Test]
    public void ParseNmcliOutput_MultipleConnections_OnlyAddsActive()
    {
        // Arrange
        var output = "wlan0:yes:HomeNet:80:2437:6:WPA2\nwlan1:no:OfficeNet:60:5240:48:WPA3\nwlan2:yes:GuestNet:45:5745:149:WPA2";
        var result = new Dictionary<string, WiFiInfo>();

        // Act
        _provider.ParseNmcliOutput(output, result);

        // Assert
        result.Should().HaveCount(2);
        result.Should().ContainKey("wlan0");
        result.Should().ContainKey("wlan2");
        result.Should().NotContainKey("wlan1");
        result["wlan0"].Ssid.Should().Be("HomeNet");
        result["wlan2"].Ssid.Should().Be("GuestNet");
    }

    [Test]
    public void ParseNmcliOutput_InactiveConnection_IsSkipped()
    {
        // Arrange
        var output = "wlan0:no:SomeNetwork:90:5180:36:WPA2";
        var result = new Dictionary<string, WiFiInfo>();

        // Act
        _provider.ParseNmcliOutput(output, result);

        // Assert
        result.Should().BeEmpty();
    }

    [Test]
    public void ParseNmcliOutput_Frequency2400MHz_DetectsAs24GHz()
    {
        // Arrange
        var output = "wlan0:yes:MyNet:70:2437:6:WPA2";
        var result = new Dictionary<string, WiFiInfo>();

        // Act
        _provider.ParseNmcliOutput(output, result);

        // Assert
        result["wlan0"].Band.Should().Be("2.4 GHz");
        result["wlan0"].FrequencyMhz.Should().Be(2437);
    }

    [Test]
    public void ParseNmcliOutput_Frequency5GHz_DetectsAs5GHz()
    {
        // Arrange
        var output = "wlan0:yes:MyNet:65:5240:48:WPA2";
        var result = new Dictionary<string, WiFiInfo>();

        // Act
        _provider.ParseNmcliOutput(output, result);

        // Assert
        result["wlan0"].Band.Should().Be("5 GHz");
        result["wlan0"].FrequencyMhz.Should().Be(5240);
    }

    [Test]
    public void ParseNmcliOutput_Frequency6GHz_DetectsAs6GHz()
    {
        // Arrange
        var output = "wlan0:yes:MyNet:55:6115:37:WPA3";
        var result = new Dictionary<string, WiFiInfo>();

        // Act
        _provider.ParseNmcliOutput(output, result);

        // Assert
        result["wlan0"].Band.Should().Be("6 GHz");
        result["wlan0"].FrequencyMhz.Should().Be(6115);
    }

    [Test]
    public void ParseNmcliOutput_MissingOptionalFields_ParsesAvailableFields()
    {
        // Arrange ÔÇö only 4 parts: DEVICE:ACTIVE:SSID:SIGNAL
        var output = "wlan0:yes:MyNet:80";
        var result = new Dictionary<string, WiFiInfo>();

        // Act
        _provider.ParseNmcliOutput(output, result);

        // Assert
        result.Should().HaveCount(1);
        result["wlan0"].Ssid.Should().Be("MyNet");
        result["wlan0"].SignalStrength.Should().Be(80);
        result["wlan0"].FrequencyMhz.Should().BeNull();
        result["wlan0"].Channel.Should().BeNull();
        result["wlan0"].Security.Should().BeNull();
    }

    [Test]
    public void ParseNmcliOutput_EmptyOutput_ReturnsEmptyDictionary()
    {
        // Arrange
        var output = "";
        var result = new Dictionary<string, WiFiInfo>();

        // Act
        _provider.ParseNmcliOutput(output, result);

        // Assert
        result.Should().BeEmpty();
    }

    [Test]
    public void ParseNmcliOutput_FrequencyWithMhzSuffix_ParsesCorrectly()
    {
        // Arrange
        var output = "wlan0:yes:MyNet:72:2412 MHz:1:WPA2";
        var result = new Dictionary<string, WiFiInfo>();

        // Act
        _provider.ParseNmcliOutput(output, result);

        // Assert
        result["wlan0"].FrequencyMhz.Should().Be(2412);
        result["wlan0"].Band.Should().Be("2.4 GHz");
    }

    // ÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉ
    // ParseIwDevInterfaces Tests
    // ÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉ

    [Test]
    public void ParseIwDevInterfaces_SingleInterface_ReturnsSingleItem()
    {
        // Arrange
        var output = """
            phy#0
                Interface wlan0
                    ifindex 3
                    wdev 0x1
                    addr 00:11:22:33:44:55
                    type managed
            """;

        // Act
        var result = _provider.ParseIwDevInterfaces(output);

        // Assert
        result.Should().HaveCount(1);
        result[0].Should().Be("wlan0");
    }

    [Test]
    public void ParseIwDevInterfaces_MultipleInterfaces_ReturnsAll()
    {
        // Arrange
        var output = """
            phy#0
                Interface wlan0
                    ifindex 3
                    wdev 0x1
            phy#1
                Interface wlan1
                    ifindex 4
                    wdev 0x2
            """;

        // Act
        var result = _provider.ParseIwDevInterfaces(output);

        // Assert
        result.Should().HaveCount(2);
        result[0].Should().Be("wlan0");
        result[1].Should().Be("wlan1");
    }

    [Test]
    public void ParseIwDevInterfaces_NoInterfaces_ReturnsEmptyList()
    {
        // Arrange
        var output = "some random output with no interface lines";

        // Act
        var result = _provider.ParseIwDevInterfaces(output);

        // Assert
        result.Should().BeEmpty();
    }

    [Test]
    public void ParseIwDevInterfaces_TypicalIwDevOutput_ExtractsInterfaceName()
    {
        // Arrange
        var output = """
            phy#0
                Interface wlp2s0
                    ifindex 3
                    wdev 0x1
                    addr aa:bb:cc:dd:ee:ff
                    ssid MyHomeNetwork
                    type managed
                    channel 6 (2437 MHz), width: 20 MHz, center1: 2437 MHz
                    txpower 20.00 dBm
            """;

        // Act
        var result = _provider.ParseIwDevInterfaces(output);

        // Assert
        result.Should().HaveCount(1);
        result[0].Should().Be("wlp2s0");
    }

    // ÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉ
    // ParseIwLinkOutput Tests
    // ÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉ

    [Test]
    public void ParseIwLinkOutput_ConnectedWithAllFields_ReturnsWiFiInfo()
    {
        // Arrange
        var output = "Connected to aa:bb:cc:dd:ee:ff (on wlan0)\n\tSSID: MyNetwork\n\tfreq: 5180\n\tsignal: -55 dBm\n\ttx bitrate: 866 MBit/s";

        // Act
        var result = _provider.ParseIwLinkOutput(output);

        // Assert
        result.Should().NotBeNull();
        result!.Ssid.Should().Be("MyNetwork");
        result.SignalDbm.Should().Be(-55);
        result.SignalStrength.Should().Be(90); // (-55 + 100) * 2 = 90
        result.FrequencyMhz.Should().Be(5180);
        result.Band.Should().Be("5 GHz");
        result.LinkSpeedMbps.Should().Be(866);
    }

    [Test]
    public void ParseIwLinkOutput_NotConnected_ReturnsNull()
    {
        // Arrange
        var output = "Not connected.";

        // Act
        var result = _provider.ParseIwLinkOutput(output);

        // Assert
        result.Should().BeNull();
    }

    [Test]
    public void ParseIwLinkOutput_NoSsidMatch_ReturnsNull()
    {
        // Arrange
        var output = "Connected to aa:bb:cc:dd:ee:ff (on wlan0)\n\tfreq: 5180\n\tsignal: -60 dBm";

        // Act
        var result = _provider.ParseIwLinkOutput(output);

        // Assert
        result.Should().BeNull();
    }

    [Test]
    public void ParseIwLinkOutput_SignalMinus30dBm_Returns100Percent()
    {
        // Arrange ÔÇö (-30 + 100) * 2 = 140, clamped to 100
        var output = "Connected to aa:bb:cc:dd:ee:ff (on wlan0)\n\tSSID: StrongSignal\n\tfreq: 2437\n\tsignal: -30 dBm";

        // Act
        var result = _provider.ParseIwLinkOutput(output);

        // Assert
        result.Should().NotBeNull();
        result!.SignalStrength.Should().Be(100);
    }

    [Test]
    public void ParseIwLinkOutput_SignalMinus50dBm_Returns100Percent()
    {
        // Arrange ÔÇö (-50 + 100) * 2 = 100
        var output = "Connected to aa:bb:cc:dd:ee:ff (on wlan0)\n\tSSID: GoodSignal\n\tfreq: 5240\n\tsignal: -50 dBm";

        // Act
        var result = _provider.ParseIwLinkOutput(output);

        // Assert
        result.Should().NotBeNull();
        result!.SignalStrength.Should().Be(100);
    }

    [Test]
    public void ParseIwLinkOutput_SignalMinus70dBm_Returns60Percent()
    {
        // Arrange ÔÇö (-70 + 100) * 2 = 60
        var output = "Connected to aa:bb:cc:dd:ee:ff (on wlan0)\n\tSSID: WeakSignal\n\tfreq: 2412\n\tsignal: -70 dBm";

        // Act
        var result = _provider.ParseIwLinkOutput(output);

        // Assert
        result.Should().NotBeNull();
        result!.SignalStrength.Should().Be(60);
    }

    [Test]
    public void ParseIwLinkOutput_SignalMinus100dBm_Returns0Percent()
    {
        // Arrange ÔÇö (-100 + 100) * 2 = 0
        var output = "Connected to aa:bb:cc:dd:ee:ff (on wlan0)\n\tSSID: VeryWeakSignal\n\tfreq: 5745\n\tsignal: -100 dBm";

        // Act
        var result = _provider.ParseIwLinkOutput(output);

        // Assert
        result.Should().NotBeNull();
        result!.SignalStrength.Should().Be(0);
    }

    [Test]
    public void ParseIwLinkOutput_Frequency2437MHz_DetectsAs24GHz()
    {
        // Arrange
        var output = "Connected to aa:bb:cc:dd:ee:ff (on wlan0)\n\tSSID: TestNet\n\tfreq: 2437\n\tsignal: -65 dBm";

        // Act
        var result = _provider.ParseIwLinkOutput(output);

        // Assert
        result.Should().NotBeNull();
        result!.Band.Should().Be("2.4 GHz");
        result.FrequencyMhz.Should().Be(2437);
    }

    [Test]
    public void ParseIwLinkOutput_MissingBitrate_ReturnsNullLinkSpeed()
    {
        // Arrange
        var output = "Connected to aa:bb:cc:dd:ee:ff (on wlan0)\n\tSSID: NoBitrateNet\n\tfreq: 5180\n\tsignal: -60 dBm";

        // Act
        var result = _provider.ParseIwLinkOutput(output);

        // Assert
        result.Should().NotBeNull();
        result!.Ssid.Should().Be("NoBitrateNet");
        result.LinkSpeedMbps.Should().BeNull();
        result.FrequencyMhz.Should().Be(5180);
        result.SignalDbm.Should().Be(-60);
    }
}
