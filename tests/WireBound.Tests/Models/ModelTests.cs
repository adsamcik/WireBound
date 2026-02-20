using AwesomeAssertions;
using WireBound.Core.Models;
using WireBound.Core.Services;

namespace WireBound.Tests.Models;

/// <summary>
/// Unit tests for core domain models
/// </summary>
public class ModelTests
{
    // ═══════════════════════════════════════════════════════════════════════
    // NetworkStats - Default Values
    // ═══════════════════════════════════════════════════════════════════════

    [Test]
    public void NetworkStats_NewInstance_HasZeroSpeeds()
    {
        // Act
        var stats = new NetworkStats();

        // Assert
        stats.DownloadSpeedBps.Should().Be(0);
        stats.UploadSpeedBps.Should().Be(0);
        stats.TotalBytesReceived.Should().Be(0);
        stats.TotalBytesSent.Should().Be(0);
        stats.SessionBytesReceived.Should().Be(0);
        stats.SessionBytesSent.Should().Be(0);
    }

    [Test]
    public void NetworkStats_NewInstance_HasDefaultAdapterAndVpnValues()
    {
        // Act
        var stats = new NetworkStats();

        // Assert
        stats.AdapterId.Should().Be(string.Empty);
        stats.IsVpnConnected.Should().BeFalse();
        stats.HasVpnTraffic.Should().BeFalse();
        stats.VpnDownloadSpeedBps.Should().Be(0);
        stats.VpnUploadSpeedBps.Should().Be(0);
        stats.ConnectedVpnAdapters.Should().BeEmpty();
        stats.ActiveVpnAdapters.Should().BeEmpty();
    }

    [Test]
    public void NetworkStats_Properties_CanBeSetAndRetrieved()
    {
        // Arrange
        var stats = new NetworkStats();

        // Act
        stats.DownloadSpeedBps = 1_000_000;
        stats.UploadSpeedBps = 500_000;
        stats.TotalBytesReceived = 10_000_000;
        stats.TotalBytesSent = 5_000_000;
        stats.SessionBytesReceived = 2_000_000;
        stats.SessionBytesSent = 1_000_000;
        stats.AdapterId = "eth0";

        // Assert
        stats.DownloadSpeedBps.Should().Be(1_000_000);
        stats.UploadSpeedBps.Should().Be(500_000);
        stats.TotalBytesReceived.Should().Be(10_000_000);
        stats.TotalBytesSent.Should().Be(5_000_000);
        stats.SessionBytesReceived.Should().Be(2_000_000);
        stats.SessionBytesSent.Should().Be(1_000_000);
        stats.AdapterId.Should().Be("eth0");
    }

    [Test]
    public void NetworkStats_IsSplitTunnelLikely_FalseWhenNoVpnTraffic()
    {
        // Arrange
        var stats = new NetworkStats { HasVpnTraffic = false };

        // Assert
        stats.IsSplitTunnelLikely.Should().BeFalse();
    }

    [Test]
    public void NetworkStats_VpnOverhead_ZeroWhenNoVpnTraffic()
    {
        // Arrange
        var stats = new NetworkStats { HasVpnTraffic = false };

        // Assert
        stats.VpnDownloadOverheadBps.Should().Be(0);
        stats.VpnUploadOverheadBps.Should().Be(0);
        stats.VpnDownloadOverheadPercent.Should().Be(0);
        stats.VpnUploadOverheadPercent.Should().Be(0);
    }

    [Test]
    public void NetworkStats_VpnOverhead_CalculatedWhenVpnActive()
    {
        // Arrange - physical > VPN but within reasonable overhead
        var stats = new NetworkStats
        {
            HasVpnTraffic = true,
            VpnDownloadSpeedBps = 1_000_000,
            PhysicalDownloadSpeedBps = 1_100_000, // 10% overhead
            VpnUploadSpeedBps = 500_000,
            PhysicalUploadSpeedBps = 550_000 // 10% overhead
        };

        // Assert
        stats.VpnDownloadOverheadBps.Should().Be(100_000);
        stats.VpnUploadOverheadBps.Should().Be(50_000);
    }

    [Test]
    public void NetworkStats_FormattedProperties_ReturnStrings()
    {
        // Arrange
        var stats = new NetworkStats
        {
            DownloadSpeedBps = 1_048_576, // 1 MB/s
            UploadSpeedBps = 524_288
        };

        // Assert - formatted properties should return non-empty strings
        stats.DownloadSpeedFormatted.Should().NotBeNullOrEmpty();
        stats.UploadSpeedFormatted.Should().NotBeNullOrEmpty();
        stats.SessionReceivedFormatted.Should().NotBeNullOrEmpty();
        stats.SessionSentFormatted.Should().NotBeNullOrEmpty();
    }

    // ═══════════════════════════════════════════════════════════════════════
    // AppSettings - Default Values
    // ═══════════════════════════════════════════════════════════════════════

    [Test]
    public void AppSettings_NewInstance_HasDefaultValues()
    {
        // Act
        var settings = new AppSettings();

        // Assert
        settings.Id.Should().Be(1); // Singleton default
        settings.PollingIntervalMs.Should().Be(1000);
        settings.SaveIntervalSeconds.Should().Be(60);
        settings.SpeedUnit.Should().Be(SpeedUnit.BytesPerSecond);
        settings.StartWithWindows.Should().BeFalse();
        settings.MinimizeToTray.Should().BeTrue();
        settings.StartMinimized.Should().BeFalse();
        settings.UseIpHelperApi.Should().BeFalse();
        settings.SelectedAdapterId.Should().Be(NetworkMonitorConstants.AutoAdapterId);
        settings.DataRetentionDays.Should().Be(365);
        settings.Theme.Should().Be("Dark");
    }

    [Test]
    public void AppSettings_NewInstance_HasDefaultPerAppTrackingValues()
    {
        // Act
        var settings = new AppSettings();

        // Assert
        settings.IsPerAppTrackingEnabled.Should().BeFalse();
        settings.AppDataRetentionDays.Should().Be(0);
        settings.AppDataAggregateAfterDays.Should().Be(7);
    }

    [Test]
    public void AppSettings_NewInstance_HasDefaultDashboardValues()
    {
        // Act
        var settings = new AppSettings();

        // Assert
        settings.ShowSystemMetricsInHeader.Should().BeTrue();
        settings.ShowCpuOverlayByDefault.Should().BeFalse();
        settings.ShowMemoryOverlayByDefault.Should().BeFalse();
        settings.ShowGpuMetrics.Should().BeTrue();
        settings.DefaultTimeRange.Should().Be("FiveMinutes");
        settings.PerformanceModeEnabled.Should().BeFalse();
        settings.ChartUpdateIntervalMs.Should().Be(1000);
    }

    [Test]
    public void AppSettings_NewInstance_HasDefaultInsightsValues()
    {
        // Act
        var settings = new AppSettings();

        // Assert
        settings.DefaultInsightsPeriod.Should().Be("ThisWeek");
        settings.ShowCorrelationInsights.Should().BeTrue();
    }

    [Test]
    public void AppSettings_SpeedUnit_CanBeChanged()
    {
        // Arrange
        var settings = new AppSettings();

        // Act
        settings.SpeedUnit = SpeedUnit.BitsPerSecond;

        // Assert
        settings.SpeedUnit.Should().Be(SpeedUnit.BitsPerSecond);
    }

    // ═══════════════════════════════════════════════════════════════════════
    // CpuStats - Default Values and Properties
    // ═══════════════════════════════════════════════════════════════════════

    [Test]
    public void CpuStats_NewInstance_HasDefaultValues()
    {
        // Act
        var stats = new CpuStats();

        // Assert
        stats.UsagePercent.Should().Be(0);
        stats.PerCoreUsagePercent.Should().BeEmpty();
        stats.ProcessorCount.Should().Be(0);
        stats.ProcessorName.Should().Be(string.Empty);
        stats.FrequencyMhz.Should().BeNull();
        stats.TemperatureCelsius.Should().BeNull();
    }

    [Test]
    [Arguments(0.0)]
    [Arguments(50.5)]
    [Arguments(100.0)]
    public void CpuStats_UsagePercent_CanBeSetAndRetrieved(double value)
    {
        // Arrange
        var stats = new CpuStats();

        // Act
        stats.UsagePercent = value;

        // Assert
        stats.UsagePercent.Should().Be(value);
    }

    [Test]
    public void CpuStats_AllProperties_CanBeSet()
    {
        // Arrange & Act
        var stats = new CpuStats
        {
            UsagePercent = 75.5,
            PerCoreUsagePercent = [80.0, 70.0, 90.0, 60.0],
            ProcessorCount = 4,
            ProcessorName = "AMD Ryzen 5 5600X",
            FrequencyMhz = 3700.0,
            TemperatureCelsius = 65.0
        };

        // Assert
        stats.UsagePercent.Should().Be(75.5);
        stats.PerCoreUsagePercent.Should().HaveCount(4);
        stats.ProcessorCount.Should().Be(4);
        stats.ProcessorName.Should().Be("AMD Ryzen 5 5600X");
        stats.FrequencyMhz.Should().Be(3700.0);
        stats.TemperatureCelsius.Should().Be(65.0);
    }

    // ═══════════════════════════════════════════════════════════════════════
    // MemoryStats - Default Values and Computed Properties
    // ═══════════════════════════════════════════════════════════════════════

    [Test]
    public void MemoryStats_NewInstance_HasDefaultValues()
    {
        // Act
        var stats = new MemoryStats();

        // Assert
        stats.TotalBytes.Should().Be(0);
        stats.UsedBytes.Should().Be(0);
        stats.AvailableBytes.Should().Be(0);
        stats.TotalVirtualBytes.Should().Be(0);
        stats.UsedVirtualBytes.Should().Be(0);
    }

    [Test]
    public void MemoryStats_UsagePercent_ComputedFromTotalAndUsed()
    {
        // Arrange
        var stats = new MemoryStats
        {
            TotalBytes = 16_000_000_000, // 16 GB
            UsedBytes = 8_000_000_000    // 8 GB
        };

        // Assert
        stats.UsagePercent.Should().Be(50.0);
    }

    [Test]
    public void MemoryStats_UsagePercent_ZeroWhenTotalIsZero()
    {
        // Arrange
        var stats = new MemoryStats { TotalBytes = 0, UsedBytes = 0 };

        // Assert
        stats.UsagePercent.Should().Be(0);
    }

    [Test]
    public void MemoryStats_UsagePercent_FullUsage()
    {
        // Arrange
        var stats = new MemoryStats
        {
            TotalBytes = 8_000_000_000,
            UsedBytes = 8_000_000_000
        };

        // Assert
        stats.UsagePercent.Should().Be(100.0);
    }

    [Test]
    public void MemoryStats_FormattedProperties_ReturnStrings()
    {
        // Arrange
        var stats = new MemoryStats
        {
            TotalBytes = 17_179_869_184, // 16 GB
            UsedBytes = 8_589_934_592,   // 8 GB
            AvailableBytes = 8_589_934_592
        };

        // Assert
        stats.TotalFormatted.Should().NotBeNullOrEmpty();
        stats.UsedFormatted.Should().NotBeNullOrEmpty();
        stats.AvailableFormatted.Should().NotBeNullOrEmpty();
    }

    // ═══════════════════════════════════════════════════════════════════════
    // SystemStats - Default Values and Sub-Objects
    // ═══════════════════════════════════════════════════════════════════════

    [Test]
    public void SystemStats_NewInstance_HasSubObjects()
    {
        // Act
        var stats = new SystemStats();

        // Assert
        stats.Cpu.Should().NotBeNull();
        stats.Memory.Should().NotBeNull();
    }

    [Test]
    public void SystemStats_NewInstance_HasTimestamp()
    {
        // Act
        var before = DateTime.Now;
        var stats = new SystemStats();
        var after = DateTime.Now;

        // Assert
        stats.Timestamp.Should().BeOnOrAfter(before);
        stats.Timestamp.Should().BeOnOrBefore(after);
    }

    [Test]
    public void SystemStats_SubObjects_AreModifiable()
    {
        // Arrange
        var stats = new SystemStats();

        // Act
        stats.Cpu.UsagePercent = 55.0;
        stats.Memory.TotalBytes = 16_000_000_000;
        stats.Memory.UsedBytes = 10_000_000_000;

        // Assert
        stats.Cpu.UsagePercent.Should().Be(55.0);
        stats.Memory.UsagePercent.Should().Be(62.5);
    }

    // ═══════════════════════════════════════════════════════════════════════
    // DailyUsage - Default Values and Computed Properties
    // ═══════════════════════════════════════════════════════════════════════

    [Test]
    public void DailyUsage_NewInstance_HasDefaultValues()
    {
        // Act
        var usage = new DailyUsage();

        // Assert
        usage.Id.Should().Be(0);
        usage.Date.Should().Be(default);
        usage.AdapterId.Should().Be(string.Empty);
        usage.BytesReceived.Should().Be(0);
        usage.BytesSent.Should().Be(0);
        usage.PeakDownloadSpeed.Should().Be(0);
        usage.PeakUploadSpeed.Should().Be(0);
    }

    [Test]
    public void DailyUsage_Date_CanBeSetAndRetrieved()
    {
        // Arrange
        var usage = new DailyUsage();
        var expectedDate = new DateOnly(2026, 6, 15);

        // Act
        usage.Date = expectedDate;

        // Assert
        usage.Date.Should().Be(expectedDate);
    }

    [Test]
    public void DailyUsage_TotalBytes_ComputedFromReceivedAndSent()
    {
        // Arrange
        var usage = new DailyUsage
        {
            BytesReceived = 1_000_000,
            BytesSent = 500_000
        };

        // Assert
        usage.TotalBytes.Should().Be(1_500_000);
    }

    [Test]
    public void DailyUsage_TotalBytes_ZeroWhenBothZero()
    {
        // Arrange
        var usage = new DailyUsage();

        // Assert
        usage.TotalBytes.Should().Be(0);
    }

    // ═══════════════════════════════════════════════════════════════════════
    // HourlyUsage - Default Values
    // ═══════════════════════════════════════════════════════════════════════

    [Test]
    public void HourlyUsage_NewInstance_HasDefaultValues()
    {
        // Act
        var usage = new HourlyUsage();

        // Assert
        usage.Id.Should().Be(0);
        usage.Hour.Should().Be(default);
        usage.AdapterId.Should().Be(string.Empty);
        usage.BytesReceived.Should().Be(0);
        usage.BytesSent.Should().Be(0);
        usage.PeakDownloadSpeed.Should().Be(0);
        usage.PeakUploadSpeed.Should().Be(0);
    }

    [Test]
    public void HourlyUsage_Hour_CanBeSetAndRetrieved()
    {
        // Arrange
        var usage = new HourlyUsage();
        var expectedHour = new DateTime(2026, 6, 15, 14, 0, 0);

        // Act
        usage.Hour = expectedHour;

        // Assert
        usage.Hour.Should().Be(expectedHour);
    }

    [Test]
    [Arguments(0, 0)]
    [Arguments(1_000_000, 500_000)]
    [Arguments(long.MaxValue / 2, long.MaxValue / 2)]
    public void HourlyUsage_BytesReceivedAndSent_CanBeSetAndRetrieved(long received, long sent)
    {
        // Arrange
        var usage = new HourlyUsage();

        // Act
        usage.BytesReceived = received;
        usage.BytesSent = sent;

        // Assert
        usage.BytesReceived.Should().Be(received);
        usage.BytesSent.Should().Be(sent);
    }

    // ═══════════════════════════════════════════════════════════════════════
    // SpeedSnapshot - Default Values
    // ═══════════════════════════════════════════════════════════════════════

    [Test]
    public void SpeedSnapshot_NewInstance_HasDefaultValues()
    {
        // Act
        var snapshot = new SpeedSnapshot();

        // Assert
        snapshot.Id.Should().Be(0);
        snapshot.Timestamp.Should().Be(default);
        snapshot.DownloadSpeedBps.Should().Be(0);
        snapshot.UploadSpeedBps.Should().Be(0);
    }

    [Test]
    public void SpeedSnapshot_Timestamp_CanBeSetAndRetrieved()
    {
        // Arrange
        var snapshot = new SpeedSnapshot();
        var expectedTimestamp = new DateTime(2026, 6, 15, 14, 30, 45);

        // Act
        snapshot.Timestamp = expectedTimestamp;

        // Assert
        snapshot.Timestamp.Should().Be(expectedTimestamp);
    }

    [Test]
    public void SpeedSnapshot_AllProperties_CanBeSet()
    {
        // Arrange & Act
        var snapshot = new SpeedSnapshot
        {
            Id = 42,
            Timestamp = new DateTime(2026, 1, 1),
            DownloadSpeedBps = 10_000_000,
            UploadSpeedBps = 5_000_000
        };

        // Assert
        snapshot.Id.Should().Be(42);
        snapshot.DownloadSpeedBps.Should().Be(10_000_000);
        snapshot.UploadSpeedBps.Should().Be(5_000_000);
    }

    // ═══════════════════════════════════════════════════════════════════════
    // NetworkAdapter - Default Values
    // ═══════════════════════════════════════════════════════════════════════

    [Test]
    public void NetworkAdapter_NewInstance_HasDefaultValues()
    {
        // Act
        var adapter = new NetworkAdapter();

        // Assert
        adapter.Id.Should().Be(string.Empty);
        adapter.Name.Should().Be(string.Empty);
        adapter.DisplayName.Should().Be(string.Empty);
        adapter.Description.Should().Be(string.Empty);
        adapter.AdapterType.Should().Be(NetworkAdapterType.Unknown);
        adapter.IsActive.Should().BeFalse();
        adapter.Speed.Should().Be(0);
        adapter.IsVirtual.Should().BeFalse();
        adapter.IsKnownVpn.Should().BeFalse();
        adapter.IsUsbTethering.Should().BeFalse();
        adapter.IsBluetoothTethering.Should().BeFalse();
        adapter.Category.Should().Be("Physical");
    }

    [Test]
    public void NetworkAdapter_Properties_CanBeSet()
    {
        // Arrange & Act
        var adapter = new NetworkAdapter
        {
            Id = "eth0",
            Name = "Ethernet",
            DisplayName = "Ethernet (Realtek)",
            Description = "Realtek PCIe GBE",
            AdapterType = NetworkAdapterType.Ethernet,
            IsActive = true,
            Speed = 1_000_000_000, // 1 Gbps
            Category = "Physical"
        };

        // Assert
        adapter.Id.Should().Be("eth0");
        adapter.Name.Should().Be("Ethernet");
        adapter.AdapterType.Should().Be(NetworkAdapterType.Ethernet);
        adapter.IsActive.Should().BeTrue();
        adapter.Speed.Should().Be(1_000_000_000);
    }

    // ═══════════════════════════════════════════════════════════════════════
    // ConnectionInfo - Default Values and Computed Properties
    // ═══════════════════════════════════════════════════════════════════════

    [Test]
    public void ConnectionInfo_NewInstance_HasDefaultValues()
    {
        // Act
        var conn = new WireBound.Core.Models.ConnectionInfo();

        // Assert
        conn.LocalAddress.Should().Be(string.Empty);
        conn.LocalPort.Should().Be(0);
        conn.RemoteAddress.Should().Be(string.Empty);
        conn.RemotePort.Should().Be(0);
        conn.ProcessId.Should().Be(0);
        conn.Protocol.Should().Be("TCP");
        conn.State.Should().Be(WireBound.Core.Models.ConnectionState.Unknown);
    }

    [Test]
    public void ConnectionInfo_Properties_CanBeSet()
    {
        // Arrange & Act
        var conn = new WireBound.Core.Models.ConnectionInfo
        {
            LocalAddress = "192.168.1.100",
            LocalPort = 54321,
            RemoteAddress = "8.8.8.8",
            RemotePort = 443,
            ProcessId = 1234,
            Protocol = "TCP",
            State = WireBound.Core.Models.ConnectionState.Established
        };

        // Assert
        conn.LocalAddress.Should().Be("192.168.1.100");
        conn.LocalPort.Should().Be(54321);
        conn.RemoteAddress.Should().Be("8.8.8.8");
        conn.RemotePort.Should().Be(443);
        conn.ProcessId.Should().Be(1234);
    }

    [Test]
    public void ConnectionInfo_IsIPv6_TrueForIPv6Address()
    {
        // Arrange
        var conn = new WireBound.Core.Models.ConnectionInfo
        {
            LocalAddress = "::1"
        };

        // Assert
        conn.IsIPv6.Should().BeTrue();
    }

    [Test]
    public void ConnectionInfo_IsIPv6_FalseForIPv4Address()
    {
        // Arrange
        var conn = new WireBound.Core.Models.ConnectionInfo
        {
            LocalAddress = "192.168.1.1"
        };

        // Assert
        conn.IsIPv6.Should().BeFalse();
    }

    [Test]
    public void ConnectionInfo_ConnectionKey_FormatsCorrectly()
    {
        // Arrange
        var conn = new WireBound.Core.Models.ConnectionInfo
        {
            Protocol = "TCP",
            LocalAddress = "192.168.1.1",
            LocalPort = 8080,
            RemoteAddress = "10.0.0.1",
            RemotePort = 443
        };

        // Assert
        conn.ConnectionKey.Should().Be("TCP:192.168.1.1:8080->10.0.0.1:443");
    }

    [Test]
    public void ConnectionInfo_RemoteEndpoint_IncludesPortWhenNonZero()
    {
        // Arrange
        var conn = new WireBound.Core.Models.ConnectionInfo
        {
            RemoteAddress = "8.8.8.8",
            RemotePort = 443
        };

        // Assert
        conn.RemoteEndpoint.Should().Be("8.8.8.8:443");
    }

    [Test]
    public void ConnectionInfo_RemoteEndpoint_OmitsPortWhenZero()
    {
        // Arrange
        var conn = new WireBound.Core.Models.ConnectionInfo
        {
            RemoteAddress = "8.8.8.8",
            RemotePort = 0
        };

        // Assert
        conn.RemoteEndpoint.Should().Be("8.8.8.8");
    }

    [Test]
    public void ConnectionInfo_LocalEndpoint_FormatsCorrectly()
    {
        // Arrange
        var conn = new WireBound.Core.Models.ConnectionInfo
        {
            LocalAddress = "127.0.0.1",
            LocalPort = 3000
        };

        // Assert
        conn.LocalEndpoint.Should().Be("127.0.0.1:3000");
    }

    // ═══════════════════════════════════════════════════════════════════════
    // AppUsageRecord - Default Values and Computed Properties
    // ═══════════════════════════════════════════════════════════════════════

    [Test]
    public void AppUsageRecord_NewInstance_HasDefaultValues()
    {
        // Act
        var record = new AppUsageRecord();

        // Assert
        record.Id.Should().Be(0);
        record.AppIdentifier.Should().Be(string.Empty);
        record.AppName.Should().Be(string.Empty);
        record.ExecutablePath.Should().Be(string.Empty);
        record.ProcessName.Should().Be(string.Empty);
        record.Timestamp.Should().Be(default);
        record.Granularity.Should().Be(UsageGranularity.Hourly);
        record.BytesReceived.Should().Be(0);
        record.BytesSent.Should().Be(0);
        record.PeakDownloadSpeed.Should().Be(0);
        record.PeakUploadSpeed.Should().Be(0);
    }

    [Test]
    public void AppUsageRecord_TotalBytes_ComputedFromReceivedAndSent()
    {
        // Arrange
        var record = new AppUsageRecord
        {
            BytesReceived = 2_000_000,
            BytesSent = 1_000_000
        };

        // Assert
        record.TotalBytes.Should().Be(3_000_000);
    }

    [Test]
    public void AppUsageRecord_Granularity_CanBeSetToDaily()
    {
        // Arrange
        var record = new AppUsageRecord();

        // Act
        record.Granularity = UsageGranularity.Daily;

        // Assert
        record.Granularity.Should().Be(UsageGranularity.Daily);
    }

    [Test]
    public void AppUsageRecord_Timestamp_CanBeSetAndRetrieved()
    {
        // Arrange
        var record = new AppUsageRecord();
        var expectedTimestamp = new DateTime(2026, 6, 15, 14, 0, 0);

        // Act
        record.Timestamp = expectedTimestamp;

        // Assert
        record.Timestamp.Should().Be(expectedTimestamp);
    }

    // ═══════════════════════════════════════════════════════════════════════
    // TimeRangeOption - Static Collections
    // ═══════════════════════════════════════════════════════════════════════

    [Test]
    public void TimeRangeOption_StandardOptions_HasExpectedCount()
    {
        // Assert
        TimeRangeOption.StandardOptions.Should().HaveCount(5);
    }

    [Test]
    public void TimeRangeOption_StandardOptions_ContainsExpectedLabels()
    {
        // Assert
        TimeRangeOption.StandardOptions[0].Label.Should().Be("30s");
        TimeRangeOption.StandardOptions[1].Label.Should().Be("1m");
        TimeRangeOption.StandardOptions[2].Label.Should().Be("5m");
        TimeRangeOption.StandardOptions[3].Label.Should().Be("15m");
        TimeRangeOption.StandardOptions[4].Label.Should().Be("1h");
    }

    [Test]
    public void TimeRangeOption_StandardOptions_HasCorrectSeconds()
    {
        // Assert
        TimeRangeOption.StandardOptions[0].Seconds.Should().Be(30);
        TimeRangeOption.StandardOptions[1].Seconds.Should().Be(60);
        TimeRangeOption.StandardOptions[2].Seconds.Should().Be(300);
        TimeRangeOption.StandardOptions[3].Seconds.Should().Be(900);
        TimeRangeOption.StandardOptions[4].Seconds.Should().Be(3600);
    }

    [Test]
    public void TimeRangeOption_ExtendedOptions_HasExpectedCount()
    {
        // Assert
        TimeRangeOption.ExtendedOptions.Should().HaveCount(4);
    }

    [Test]
    public void TimeRangeOption_AllOptions_HaveDescriptions()
    {
        // Assert
        foreach (var option in TimeRangeOption.StandardOptions)
        {
            option.Description.Should().NotBeNullOrEmpty();
        }

        foreach (var option in TimeRangeOption.ExtendedOptions)
        {
            option.Description.Should().NotBeNullOrEmpty();
        }
    }

    // ═══════════════════════════════════════════════════════════════════════
    // SpeedUnit Enum - Values Exist
    // ═══════════════════════════════════════════════════════════════════════

    [Test]
    public void SpeedUnit_BytesPerSecond_Exists()
    {
        // Assert
        SpeedUnit.BytesPerSecond.Should().Be(SpeedUnit.BytesPerSecond);
        ((int)SpeedUnit.BytesPerSecond).Should().Be(0);
    }

    [Test]
    public void SpeedUnit_BitsPerSecond_Exists()
    {
        // Assert
        SpeedUnit.BitsPerSecond.Should().Be(SpeedUnit.BitsPerSecond);
        ((int)SpeedUnit.BitsPerSecond).Should().Be(1);
    }

    // ═══════════════════════════════════════════════════════════════════════
    // NetworkAdapterType Enum - Values Exist
    // ═══════════════════════════════════════════════════════════════════════

    [Test]
    public void NetworkAdapterType_HasExpectedValues()
    {
        // Assert
        Enum.GetValues<NetworkAdapterType>().Should().HaveCount(6);
        ((int)NetworkAdapterType.Unknown).Should().Be(0);
        ((int)NetworkAdapterType.Ethernet).Should().Be(1);
        ((int)NetworkAdapterType.WiFi).Should().Be(2);
    }

    // ═══════════════════════════════════════════════════════════════════════
    // ConnectionState Enum - Values Exist
    // ═══════════════════════════════════════════════════════════════════════

    [Test]
    public void ConnectionState_HasExpectedValues()
    {
        // Assert
        WireBound.Core.Models.ConnectionState.Unknown.Should().Be(WireBound.Core.Models.ConnectionState.Unknown);
        ((int)WireBound.Core.Models.ConnectionState.Established).Should().Be(5);
        ((int)WireBound.Core.Models.ConnectionState.TimeWait).Should().Be(11);
    }

    // ═══════════════════════════════════════════════════════════════════════
    // UsageGranularity Enum - Values Exist
    // ═══════════════════════════════════════════════════════════════════════

    [Test]
    public void UsageGranularity_HasExpectedValues()
    {
        // Assert
        UsageGranularity.Hourly.Should().Be(UsageGranularity.Hourly);
        UsageGranularity.Daily.Should().Be(UsageGranularity.Daily);
        ((int)UsageGranularity.Hourly).Should().Be(0);
        ((int)UsageGranularity.Daily).Should().Be(1);
    }
}
