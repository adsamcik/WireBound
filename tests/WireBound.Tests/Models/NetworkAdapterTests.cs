using WireBound.Models;

namespace WireBound.Tests.Models;

/// <summary>
/// Tests for NetworkAdapter model
/// </summary>
public class NetworkAdapterTests
{
    #region Default Values Tests

    [Test]
    public async Task NetworkAdapter_DefaultValues_AreCorrect()
    {
        // Arrange & Act
        var adapter = new NetworkAdapter();

        // Assert
        await Assert.That(adapter.Id).IsEqualTo(string.Empty);
        await Assert.That(adapter.Name).IsEqualTo(string.Empty);
        await Assert.That(adapter.Description).IsEqualTo(string.Empty);
        await Assert.That(adapter.AdapterType).IsEqualTo(NetworkAdapterType.Unknown);
        await Assert.That(adapter.IsActive).IsFalse();
        await Assert.That(adapter.Speed).IsEqualTo(0);
    }

    #endregion

    #region Property Assignment Tests

    [Test]
    public async Task NetworkAdapter_PropertyAssignment_WorksCorrectly()
    {
        // Arrange & Act
        var adapter = new NetworkAdapter
        {
            Id = "nic-12345",
            Name = "Ethernet 1",
            Description = "Intel I225-V Ethernet Controller",
            AdapterType = NetworkAdapterType.Ethernet,
            IsActive = true,
            Speed = 2_500_000_000 // 2.5 Gbps
        };

        // Assert
        await Assert.That(adapter.Id).IsEqualTo("nic-12345");
        await Assert.That(adapter.Name).IsEqualTo("Ethernet 1");
        await Assert.That(adapter.Description).IsEqualTo("Intel I225-V Ethernet Controller");
        await Assert.That(adapter.AdapterType).IsEqualTo(NetworkAdapterType.Ethernet);
        await Assert.That(adapter.IsActive).IsTrue();
        await Assert.That(adapter.Speed).IsEqualTo(2_500_000_000);
    }

    #endregion

    #region NetworkAdapterType Tests

    [Test]
    public async Task NetworkAdapterType_EthernetAdapter_HasCorrectType()
    {
        // Arrange
        var adapter = new NetworkAdapter { AdapterType = NetworkAdapterType.Ethernet };

        // Assert
        await Assert.That(adapter.AdapterType).IsEqualTo(NetworkAdapterType.Ethernet);
    }

    [Test]
    public async Task NetworkAdapterType_WiFiAdapter_HasCorrectType()
    {
        // Arrange
        var adapter = new NetworkAdapter { AdapterType = NetworkAdapterType.WiFi };

        // Assert
        await Assert.That(adapter.AdapterType).IsEqualTo(NetworkAdapterType.WiFi);
    }

    [Test]
    public async Task NetworkAdapterType_LoopbackAdapter_HasCorrectType()
    {
        // Arrange
        var adapter = new NetworkAdapter { AdapterType = NetworkAdapterType.Loopback };

        // Assert
        await Assert.That(adapter.AdapterType).IsEqualTo(NetworkAdapterType.Loopback);
    }

    [Test]
    public async Task NetworkAdapterType_TunnelAdapter_HasCorrectType()
    {
        // Arrange
        var adapter = new NetworkAdapter { AdapterType = NetworkAdapterType.Tunnel };

        // Assert
        await Assert.That(adapter.AdapterType).IsEqualTo(NetworkAdapterType.Tunnel);
    }

    [Test]
    public async Task NetworkAdapterType_OtherAdapter_HasCorrectType()
    {
        // Arrange
        var adapter = new NetworkAdapter { AdapterType = NetworkAdapterType.Other };

        // Assert
        await Assert.That(adapter.AdapterType).IsEqualTo(NetworkAdapterType.Other);
    }

    [Test]
    public async Task NetworkAdapterType_AllValues_AreDefined()
    {
        // Assert - Verify all expected enum values exist
        var enumValues = Enum.GetValues<NetworkAdapterType>();
        await Assert.That(enumValues).Contains(NetworkAdapterType.Unknown);
        await Assert.That(enumValues).Contains(NetworkAdapterType.Ethernet);
        await Assert.That(enumValues).Contains(NetworkAdapterType.WiFi);
        await Assert.That(enumValues).Contains(NetworkAdapterType.Loopback);
        await Assert.That(enumValues).Contains(NetworkAdapterType.Tunnel);
        await Assert.That(enumValues).Contains(NetworkAdapterType.Other);
    }

    #endregion

    #region IsActive Tests

    [Test]
    public async Task NetworkAdapter_IsActive_CanBeTrue()
    {
        // Arrange
        var adapter = new NetworkAdapter { IsActive = true };

        // Assert
        await Assert.That(adapter.IsActive).IsTrue();
    }

    [Test]
    public async Task NetworkAdapter_IsActive_CanBeFalse()
    {
        // Arrange - Disconnected adapter
        var adapter = new NetworkAdapter { IsActive = false };

        // Assert
        await Assert.That(adapter.IsActive).IsFalse();
    }

    #endregion

    #region Speed Tests

    [Test]
    public async Task NetworkAdapter_Speed_GigabitEthernet()
    {
        // Arrange - 1 Gbps
        var adapter = new NetworkAdapter { Speed = 1_000_000_000 };

        // Assert
        await Assert.That(adapter.Speed).IsEqualTo(1_000_000_000);
    }

    [Test]
    public async Task NetworkAdapter_Speed_TwoPointFiveGigabit()
    {
        // Arrange - 2.5 Gbps
        var adapter = new NetworkAdapter { Speed = 2_500_000_000 };

        // Assert
        await Assert.That(adapter.Speed).IsEqualTo(2_500_000_000);
    }

    [Test]
    public async Task NetworkAdapter_Speed_TenGigabit()
    {
        // Arrange - 10 Gbps
        var adapter = new NetworkAdapter { Speed = 10_000_000_000 };

        // Assert
        await Assert.That(adapter.Speed).IsEqualTo(10_000_000_000);
    }

    [Test]
    public async Task NetworkAdapter_Speed_WiFi6()
    {
        // Arrange - WiFi 6 (~2.4 Gbps theoretical max)
        var adapter = new NetworkAdapter 
        { 
            AdapterType = NetworkAdapterType.WiFi,
            Speed = 2_400_000_000 
        };

        // Assert
        await Assert.That(adapter.Speed).IsEqualTo(2_400_000_000);
    }

    [Test]
    public async Task NetworkAdapter_Speed_ZeroWhenDisconnected()
    {
        // Arrange
        var adapter = new NetworkAdapter 
        { 
            IsActive = false,
            Speed = 0 
        };

        // Assert
        await Assert.That(adapter.Speed).IsEqualTo(0);
    }

    #endregion

    #region Typical Adapter Configurations

    [Test]
    public async Task NetworkAdapter_TypicalEthernetConfig_IsValid()
    {
        // Arrange
        var adapter = new NetworkAdapter
        {
            Id = "{GUID-HERE}",
            Name = "Ethernet",
            Description = "Realtek PCIe GbE Family Controller",
            AdapterType = NetworkAdapterType.Ethernet,
            IsActive = true,
            Speed = 1_000_000_000 // 1 Gbps
        };

        // Assert
        await Assert.That(adapter.AdapterType).IsEqualTo(NetworkAdapterType.Ethernet);
        await Assert.That(adapter.IsActive).IsTrue();
        await Assert.That(adapter.Speed).IsGreaterThan(0);
    }

    [Test]
    public async Task NetworkAdapter_TypicalWiFiConfig_IsValid()
    {
        // Arrange
        var adapter = new NetworkAdapter
        {
            Id = "{WIFI-GUID}",
            Name = "Wi-Fi",
            Description = "Intel Wi-Fi 6E AX211 160MHz",
            AdapterType = NetworkAdapterType.WiFi,
            IsActive = true,
            Speed = 2_400_000_000 // WiFi 6
        };

        // Assert
        await Assert.That(adapter.AdapterType).IsEqualTo(NetworkAdapterType.WiFi);
        await Assert.That(adapter.IsActive).IsTrue();
    }

    [Test]
    public async Task NetworkAdapter_LoopbackConfig_IsValid()
    {
        // Arrange
        var adapter = new NetworkAdapter
        {
            Id = "loopback",
            Name = "Loopback Pseudo-Interface 1",
            Description = "Software Loopback Interface 1",
            AdapterType = NetworkAdapterType.Loopback,
            IsActive = true,
            Speed = 1_073_741_824 // Loopback typically shows 1 GB
        };

        // Assert
        await Assert.That(adapter.AdapterType).IsEqualTo(NetworkAdapterType.Loopback);
    }

    #endregion
}
