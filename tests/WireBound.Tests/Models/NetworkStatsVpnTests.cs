using WireBound.Core.Models;

namespace WireBound.Tests.Models;

/// <summary>
/// Tests for NetworkStats computed VPN properties:
/// IsSplitTunnelLikely, VpnDownload/UploadOverheadBps, VpnDownload/UploadOverheadPercent
/// </summary>
public class NetworkStatsVpnTests
{
    // ÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉ
    // IsSplitTunnelLikely
    // ÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉ

    [Test]
    public void IsSplitTunnelLikely_NoVpnTraffic_ReturnsFalse()
    {
        // Arrange
        var stats = new NetworkStats
        {
            HasVpnTraffic = false,
            VpnDownloadSpeedBps = 1_000_000,
            PhysicalDownloadSpeedBps = 5_000_000,
        };

        // Act
        var result = stats.IsSplitTunnelLikely;

        // Assert
        result.Should().BeFalse();
    }

    [Test]
    public void IsSplitTunnelLikely_PhysicalApproximatelyEqualsVpn_ReturnsFalse()
    {
        // Arrange ÔÇö physical is 1.1├ù VPN (normal ~10% overhead)
        var stats = new NetworkStats
        {
            HasVpnTraffic = true,
            VpnDownloadSpeedBps = 1_000_000,
            PhysicalDownloadSpeedBps = 1_100_000,
            VpnUploadSpeedBps = 500_000,
            PhysicalUploadSpeedBps = 550_000,
        };

        // Act
        var result = stats.IsSplitTunnelLikely;

        // Assert
        result.Should().BeFalse();
    }

    [Test]
    public void IsSplitTunnelLikely_PhysicalDownloadExceedsThreshold_ReturnsTrue()
    {
        // Arrange ÔÇö physical download > 1.5├ù VPN download (split tunnel)
        var stats = new NetworkStats
        {
            HasVpnTraffic = true,
            VpnDownloadSpeedBps = 1_000_000,
            PhysicalDownloadSpeedBps = 2_000_000,
            VpnUploadSpeedBps = 0,
            PhysicalUploadSpeedBps = 0,
        };

        // Act
        var result = stats.IsSplitTunnelLikely;

        // Assert
        result.Should().BeTrue();
    }

    [Test]
    public void IsSplitTunnelLikely_PhysicalUploadExceedsThreshold_ReturnsTrue()
    {
        // Arrange ÔÇö physical upload > 1.5├ù VPN upload (split tunnel)
        var stats = new NetworkStats
        {
            HasVpnTraffic = true,
            VpnDownloadSpeedBps = 0,
            PhysicalDownloadSpeedBps = 0,
            VpnUploadSpeedBps = 1_000_000,
            PhysicalUploadSpeedBps = 2_000_000,
        };

        // Act
        var result = stats.IsSplitTunnelLikely;

        // Assert
        result.Should().BeTrue();
    }

    [Test]
    public void IsSplitTunnelLikely_PhysicalExactly1Point5TimesVpn_ReturnsFalse()
    {
        // Arrange ÔÇö boundary: physical == VPN ├ù 1.5 (not strictly greater than)
        var stats = new NetworkStats
        {
            HasVpnTraffic = true,
            VpnDownloadSpeedBps = 1_000_000,
            PhysicalDownloadSpeedBps = 1_500_000,
            VpnUploadSpeedBps = 0,
            PhysicalUploadSpeedBps = 0,
        };

        // Act
        var result = stats.IsSplitTunnelLikely;

        // Assert
        result.Should().BeFalse();
    }

    [Test]
    public void IsSplitTunnelLikely_PhysicalJustAbove1Point5TimesVpn_ReturnsTrue()
    {
        // Arrange ÔÇö boundary: physical just over VPN ├ù 1.5
        var stats = new NetworkStats
        {
            HasVpnTraffic = true,
            VpnDownloadSpeedBps = 1_000_000,
            PhysicalDownloadSpeedBps = 1_500_001,
            VpnUploadSpeedBps = 0,
            PhysicalUploadSpeedBps = 0,
        };

        // Act
        var result = stats.IsSplitTunnelLikely;

        // Assert
        result.Should().BeTrue();
    }

    [Test]
    public void IsSplitTunnelLikely_HasVpnTrafficFalseWithSpeeds_ReturnsFalse()
    {
        // Arrange ÔÇö speeds set but HasVpnTraffic is false
        var stats = new NetworkStats
        {
            HasVpnTraffic = false,
            VpnDownloadSpeedBps = 1_000_000,
            PhysicalDownloadSpeedBps = 5_000_000,
            VpnUploadSpeedBps = 500_000,
            PhysicalUploadSpeedBps = 3_000_000,
        };

        // Act
        var result = stats.IsSplitTunnelLikely;

        // Assert
        result.Should().BeFalse();
    }

    // ÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉ
    // VpnDownloadOverheadBps / VpnUploadOverheadBps
    // ÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉ

    [Test]
    public void VpnDownloadOverheadBps_NoVpnTraffic_ReturnsZero()
    {
        // Arrange
        var stats = new NetworkStats
        {
            HasVpnTraffic = false,
            VpnDownloadSpeedBps = 1_000_000,
            PhysicalDownloadSpeedBps = 1_100_000,
        };

        // Act
        var result = stats.VpnDownloadOverheadBps;

        // Assert
        result.Should().Be(0);
    }

    [Test]
    public void VpnUploadOverheadBps_NoVpnTraffic_ReturnsZero()
    {
        // Arrange
        var stats = new NetworkStats
        {
            HasVpnTraffic = false,
            VpnUploadSpeedBps = 500_000,
            PhysicalUploadSpeedBps = 550_000,
        };

        // Act
        var result = stats.VpnUploadOverheadBps;

        // Assert
        result.Should().Be(0);
    }

    [Test]
    public void VpnDownloadOverheadBps_VpnDownloadSpeedZero_ReturnsZero()
    {
        // Arrange
        var stats = new NetworkStats
        {
            HasVpnTraffic = true,
            VpnDownloadSpeedBps = 0,
            PhysicalDownloadSpeedBps = 1_000_000,
        };

        // Act
        var result = stats.VpnDownloadOverheadBps;

        // Assert
        result.Should().Be(0);
    }

    [Test]
    public void VpnDownloadOverheadBps_NormalVpn_ReturnsPhysicalMinusVpn()
    {
        // Arrange ÔÇö no split tunnel: overhead = physical - vpn
        var stats = new NetworkStats
        {
            HasVpnTraffic = true,
            VpnDownloadSpeedBps = 1_000_000,
            PhysicalDownloadSpeedBps = 1_150_000,
            VpnUploadSpeedBps = 0,
            PhysicalUploadSpeedBps = 0,
        };

        // Act
        var result = stats.VpnDownloadOverheadBps;

        // Assert
        result.Should().Be(150_000);
    }

    [Test]
    public void VpnUploadOverheadBps_NormalVpn_ReturnsPhysicalMinusVpn()
    {
        // Arrange ÔÇö no split tunnel: overhead = physical - vpn
        var stats = new NetworkStats
        {
            HasVpnTraffic = true,
            VpnDownloadSpeedBps = 0,
            PhysicalDownloadSpeedBps = 0,
            VpnUploadSpeedBps = 500_000,
            PhysicalUploadSpeedBps = 575_000,
        };

        // Act
        var result = stats.VpnUploadOverheadBps;

        // Assert
        result.Should().Be(75_000);
    }

    [Test]
    public void VpnDownloadOverheadBps_SplitTunnel_ReturnsEstimatedOverhead()
    {
        // Arrange ÔÇö split tunnel detected: overhead = vpn ├ù 0.10
        var stats = new NetworkStats
        {
            HasVpnTraffic = true,
            VpnDownloadSpeedBps = 1_000_000,
            PhysicalDownloadSpeedBps = 3_000_000,
            VpnUploadSpeedBps = 0,
            PhysicalUploadSpeedBps = 0,
        };

        // Act
        var result = stats.VpnDownloadOverheadBps;

        // Assert
        result.Should().Be(100_000); // 1_000_000 ├ù 0.10
    }

    [Test]
    public void VpnUploadOverheadBps_SplitTunnel_ReturnsEstimatedOverhead()
    {
        // Arrange ÔÇö split tunnel detected via upload
        var stats = new NetworkStats
        {
            HasVpnTraffic = true,
            VpnDownloadSpeedBps = 0,
            PhysicalDownloadSpeedBps = 0,
            VpnUploadSpeedBps = 2_000_000,
            PhysicalUploadSpeedBps = 5_000_000,
        };

        // Act
        var result = stats.VpnUploadOverheadBps;

        // Assert
        result.Should().Be(200_000); // 2_000_000 ├ù 0.10
    }

    [Test]
    public void VpnDownloadOverheadBps_PhysicalLessThanVpn_ReturnsZero()
    {
        // Arrange ÔÇö edge case: physical < vpn, Math.Max(0, negative) = 0
        var stats = new NetworkStats
        {
            HasVpnTraffic = true,
            VpnDownloadSpeedBps = 1_000_000,
            PhysicalDownloadSpeedBps = 900_000,
            VpnUploadSpeedBps = 0,
            PhysicalUploadSpeedBps = 0,
        };

        // Act
        var result = stats.VpnDownloadOverheadBps;

        // Assert
        result.Should().Be(0);
    }

    [Test]
    public void VpnUploadOverheadBps_PhysicalLessThanVpn_ReturnsZero()
    {
        // Arrange ÔÇö edge case: physical < vpn
        var stats = new NetworkStats
        {
            HasVpnTraffic = true,
            VpnDownloadSpeedBps = 0,
            PhysicalDownloadSpeedBps = 0,
            VpnUploadSpeedBps = 500_000,
            PhysicalUploadSpeedBps = 400_000,
        };

        // Act
        var result = stats.VpnUploadOverheadBps;

        // Assert
        result.Should().Be(0);
    }

    // ÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉ
    // VpnDownloadOverheadPercent / VpnUploadOverheadPercent
    // ÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉ

    [Test]
    public void VpnDownloadOverheadPercent_NoVpnTraffic_ReturnsZero()
    {
        // Arrange
        var stats = new NetworkStats
        {
            HasVpnTraffic = false,
            VpnDownloadSpeedBps = 0,
        };

        // Act
        var result = stats.VpnDownloadOverheadPercent;

        // Assert
        result.Should().Be(0);
    }

    [Test]
    public void VpnUploadOverheadPercent_NoVpnTraffic_ReturnsZero()
    {
        // Arrange
        var stats = new NetworkStats
        {
            HasVpnTraffic = false,
            VpnUploadSpeedBps = 0,
        };

        // Act
        var result = stats.VpnUploadOverheadPercent;

        // Assert
        result.Should().Be(0);
    }

    [Test]
    public void VpnDownloadOverheadPercent_VpnDownloadSpeedZero_ReturnsZero()
    {
        // Arrange
        var stats = new NetworkStats
        {
            HasVpnTraffic = true,
            VpnDownloadSpeedBps = 0,
            PhysicalDownloadSpeedBps = 1_000_000,
        };

        // Act
        var result = stats.VpnDownloadOverheadPercent;

        // Assert
        result.Should().Be(0);
    }

    [Test]
    public void VpnDownloadOverheadPercent_NormalOverhead_ReturnsCalculatedPercent()
    {
        // Arrange ÔÇö overhead = 150_000, vpn = 1_000_000 ÔåÆ 15.0%
        var stats = new NetworkStats
        {
            HasVpnTraffic = true,
            VpnDownloadSpeedBps = 1_000_000,
            PhysicalDownloadSpeedBps = 1_150_000,
            VpnUploadSpeedBps = 0,
            PhysicalUploadSpeedBps = 0,
        };

        // Act
        var result = stats.VpnDownloadOverheadPercent;

        // Assert
        result.Should().Be(15.0);
    }

    [Test]
    public void VpnUploadOverheadPercent_NormalOverhead_ReturnsCalculatedPercent()
    {
        // Arrange ÔÇö overhead = 75_000, vpn = 500_000 ÔåÆ 15.0%
        var stats = new NetworkStats
        {
            HasVpnTraffic = true,
            VpnDownloadSpeedBps = 0,
            PhysicalDownloadSpeedBps = 0,
            VpnUploadSpeedBps = 500_000,
            PhysicalUploadSpeedBps = 575_000,
        };

        // Act
        var result = stats.VpnUploadOverheadPercent;

        // Assert
        result.Should().Be(15.0);
    }

    [Test]
    public void VpnDownloadOverheadPercent_HighOverhead_CappedAt50Percent()
    {
        // Arrange ÔÇö physical just under split tunnel threshold but high overhead
        // VPN = 1_000_000, physical = 1_499_999 ÔåÆ overhead = 499_999 ÔåÆ ~50.0%
        // Threshold is > 1_500_000 so no split tunnel
        var stats = new NetworkStats
        {
            HasVpnTraffic = true,
            VpnDownloadSpeedBps = 1_000_000,
            PhysicalDownloadSpeedBps = 1_499_999,
            VpnUploadSpeedBps = 0,
            PhysicalUploadSpeedBps = 0,
        };

        // Act
        var result = stats.VpnDownloadOverheadPercent;

        // Assert ÔÇö 499_999/1_000_000 * 100 = 49.9999 rounded to 50.0, capped at 50
        result.Should().Be(50.0);
    }

    [Test]
    public void VpnDownloadOverheadPercent_SplitTunnel_ReturnsTenPercent()
    {
        // Arrange ÔÇö split tunnel: overhead = vpn ├ù 0.10 ÔåÆ 10%
        var stats = new NetworkStats
        {
            HasVpnTraffic = true,
            VpnDownloadSpeedBps = 1_000_000,
            PhysicalDownloadSpeedBps = 3_000_000,
            VpnUploadSpeedBps = 0,
            PhysicalUploadSpeedBps = 0,
        };

        // Act
        var result = stats.VpnDownloadOverheadPercent;

        // Assert ÔÇö 100_000 / 1_000_000 * 100 = 10.0%
        result.Should().Be(10.0);
    }

    [Test]
    public void VpnUploadOverheadPercent_SplitTunnel_ReturnsTenPercent()
    {
        // Arrange ÔÇö split tunnel via upload
        var stats = new NetworkStats
        {
            HasVpnTraffic = true,
            VpnDownloadSpeedBps = 0,
            PhysicalDownloadSpeedBps = 0,
            VpnUploadSpeedBps = 2_000_000,
            PhysicalUploadSpeedBps = 5_000_000,
        };

        // Act
        var result = stats.VpnUploadOverheadPercent;

        // Assert ÔÇö 200_000 / 2_000_000 * 100 = 10.0%
        result.Should().Be(10.0);
    }
}
