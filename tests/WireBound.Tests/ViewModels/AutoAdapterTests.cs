using Microsoft.Extensions.Logging;
using WireBound.Avalonia.ViewModels;
using WireBound.Core;
using WireBound.Core.Helpers;
using WireBound.Core.Models;
using WireBound.Core.Services;
using WireBound.Tests.Fixtures;

namespace WireBound.Tests.ViewModels;

/// <summary>
/// Comprehensive tests for the auto network adapter feature including:
/// - AdapterDisplayItem Auto factory and updates
/// - SecondaryAdapterInfo model
/// - OverviewViewModel auto adapter behavior (selection, notification, secondary tracking)
/// - SettingsViewModel auto adapter in dropdown
/// - NetworkMonitorConstants
/// - AppSettings defaults
/// - NetworkStats resolved adapter properties
/// </summary>
public class AutoAdapterTests : IAsyncDisposable
{
    private readonly IUiDispatcher _dispatcher = new SynchronousDispatcher();
    private readonly INetworkMonitorService _networkMonitor;
    private readonly ISystemMonitorService _systemMonitor;
    private readonly INavigationService _navigationService;
    private readonly IDataPersistenceService _persistence;
    private readonly ILogger<OverviewViewModel> _logger;
    private readonly List<OverviewViewModel> _createdViewModels = [];

    public AutoAdapterTests()
    {
        _networkMonitor = Substitute.For<INetworkMonitorService>();
        _systemMonitor = Substitute.For<ISystemMonitorService>();
        _navigationService = Substitute.For<INavigationService>();
        _persistence = Substitute.For<IDataPersistenceService>();
        _logger = Substitute.For<ILogger<OverviewViewModel>>();
        SetupDefaultMocks();
    }

    private void SetupDefaultMocks()
    {
        _networkMonitor.GetAdapters(Arg.Any<bool>()).Returns(new List<NetworkAdapter>());
        _networkMonitor.GetCurrentStats().Returns(new NetworkStats
        {
            Timestamp = DateTime.Now,
            DownloadSpeedBps = 0,
            UploadSpeedBps = 0,
            SessionBytesReceived = 0,
            SessionBytesSent = 0,
            TotalBytesReceived = 0,
            TotalBytesSent = 0
        });
        _networkMonitor.GetAllAdapterStats().Returns(new Dictionary<string, NetworkStats>());
        _networkMonitor.GetPrimaryAdapterId().Returns(string.Empty);
        _systemMonitor.GetCurrentStats().Returns(new SystemStats
        {
            Timestamp = DateTime.Now,
            Cpu = new CpuStats { UsagePercent = 0 },
            Memory = new MemoryStats { TotalBytes = 100, UsedBytes = 0 }
        });
        _persistence.GetTodayUsageAsync().Returns((0L, 0L));
        _persistence.GetSettingsAsync().Returns(new AppSettings());
        _navigationService.CurrentView.Returns(Routes.Overview);
    }

    private OverviewViewModel CreateViewModel()
    {
        var vm = new OverviewViewModel(_dispatcher, _networkMonitor, _systemMonitor, _navigationService, _persistence, _logger);
        _createdViewModels.Add(vm);
        return vm;
    }

    #region AdapterDisplayItem.CreateAuto Tests

    [Test]
    public void CreateAuto_WithResolvedName_ContainsNameInDisplayName()
    {
        var item = AdapterDisplayItem.CreateAuto("Ethernet 2");

        item.DisplayName.Should().Contain("Auto");
        item.DisplayName.Should().Contain("Ethernet 2");
    }

    [Test]
    public void CreateAuto_WithNullName_ShowsDetecting()
    {
        var item = AdapterDisplayItem.CreateAuto(null);

        item.DisplayName.Should().Contain("detecting");
    }

    [Test]
    public void CreateAuto_WithEmptyName_ShowsDetecting()
    {
        var item = AdapterDisplayItem.CreateAuto("");

        item.DisplayName.Should().Contain("detecting");
    }

    [Test]
    public void CreateAuto_HasAutoAdapterId()
    {
        var item = AdapterDisplayItem.CreateAuto("Test");

        item.Id.Should().Be(NetworkMonitorConstants.AutoAdapterId);
        item.IsAuto.Should().BeTrue();
    }

    [Test]
    public void CreateAuto_IsActive()
    {
        var item = AdapterDisplayItem.CreateAuto();

        item.IsActive.Should().BeTrue();
    }

    [Test]
    public void CreateAuto_HasAutoCategory()
    {
        var item = AdapterDisplayItem.CreateAuto();

        item.Category.Should().Be("Auto");
    }

    [Test]
    public void CreateAuto_NameIsAuto()
    {
        var item = AdapterDisplayItem.CreateAuto();

        item.Name.Should().Be("Auto");
    }

    [Test]
    public void CreateAuto_HasDescription()
    {
        var item = AdapterDisplayItem.CreateAuto();

        item.Description.Should().NotBeNullOrEmpty();
        item.Description.Should().Contain("primary internet adapter");
    }

    #endregion

    #region AdapterDisplayItem.UpdateAutoResolvedName Tests

    [Test]
    public void UpdateAutoResolvedName_UpdatesDisplayNameOnAutoItem()
    {
        var item = AdapterDisplayItem.CreateAuto();
        item.UpdateAutoResolvedName("WiFi 5");

        item.DisplayName.Should().Contain("WiFi 5");
        item.DisplayName.Should().Contain("Auto");
    }

    [Test]
    public void UpdateAutoResolvedName_WithEmptyName_ShowsDetecting()
    {
        var item = AdapterDisplayItem.CreateAuto("Ethernet");
        item.UpdateAutoResolvedName("");

        item.DisplayName.Should().Contain("detecting");
    }

    [Test]
    public void UpdateAutoResolvedName_WithNullName_ShowsDetecting()
    {
        var item = AdapterDisplayItem.CreateAuto("Ethernet");
        item.UpdateAutoResolvedName(null!);

        item.DisplayName.Should().Contain("detecting");
    }

    [Test]
    public void UpdateAutoResolvedName_OnNonAutoItem_DoesNotChangeDisplayName()
    {
        var adapter = new NetworkAdapter { Id = "eth0", Name = "Ethernet", DisplayName = "Ethernet" };
        var item = new AdapterDisplayItem(adapter);
        var original = item.DisplayName;

        item.UpdateAutoResolvedName("WiFi");

        item.DisplayName.Should().Be(original);
    }

    [Test]
    public void UpdateAutoResolvedName_RaisesPropertyChanged()
    {
        var item = AdapterDisplayItem.CreateAuto();
        using var monitor = item.Monitor();

        item.UpdateAutoResolvedName("Ethernet");

        monitor.Should().RaisePropertyChangeFor(x => x.DisplayName);
    }

    [Test]
    public void UpdateAutoResolvedName_CanBeCalledMultipleTimes()
    {
        var item = AdapterDisplayItem.CreateAuto();

        item.UpdateAutoResolvedName("Ethernet");
        item.DisplayName.Should().Contain("Ethernet");

        item.UpdateAutoResolvedName("WiFi");
        item.DisplayName.Should().Contain("WiFi");
        item.DisplayName.Should().NotContain("Ethernet");
    }

    #endregion

    #region AdapterDisplayItem.IsAuto Tests

    [Test]
    public void IsAuto_TrueForAutoItem()
    {
        var item = AdapterDisplayItem.CreateAuto();
        item.IsAuto.Should().BeTrue();
    }

    [Test]
    public void IsAuto_FalseForRegularAdapter()
    {
        var adapter = new NetworkAdapter { Id = "eth0", Name = "Ethernet" };
        var item = new AdapterDisplayItem(adapter);
        item.IsAuto.Should().BeFalse();
    }

    [Test]
    public void IsAuto_FalseForAdapterWithNonAutoId()
    {
        var adapter = new NetworkAdapter { Id = "some-other-id", Name = "Auto" };
        var item = new AdapterDisplayItem(adapter);
        item.IsAuto.Should().BeFalse();
    }

    #endregion

    #region SecondaryAdapterInfo Tests

    [Test]
    public void SecondaryAdapterInfo_AllRequiredPropertiesSet()
    {
        var info = new SecondaryAdapterInfo
        {
            AdapterId = "vpn0",
            Name = "WireGuard",
            Icon = "🔐",
            DownloadSpeed = "10 MB/s",
            UploadSpeed = "5 MB/s",
            DownloadBps = 10_000_000,
            UploadBps = 5_000_000,
            IsVpn = true,
            ColorHex = "#A855F7"
        };

        info.AdapterId.Should().Be("vpn0");
        info.Name.Should().Be("WireGuard");
        info.Icon.Should().Be("🔐");
        info.DownloadSpeed.Should().Be("10 MB/s");
        info.UploadSpeed.Should().Be("5 MB/s");
        info.DownloadBps.Should().Be(10_000_000);
        info.UploadBps.Should().Be(5_000_000);
        info.IsVpn.Should().BeTrue();
        info.ColorHex.Should().Be("#A855F7");
    }

    [Test]
    public void SecondaryAdapterInfo_IsVpn_DefaultsFalse()
    {
        var info = new SecondaryAdapterInfo
        {
            AdapterId = "eth0",
            Name = "Ethernet",
            Icon = "🔌",
            DownloadSpeed = "0 B/s",
            UploadSpeed = "0 B/s",
            ColorHex = "#3B82F6"
        };

        info.IsVpn.Should().BeFalse();
    }

    [Test]
    public void SecondaryAdapterInfo_SpeedBps_DefaultsToZero()
    {
        var info = new SecondaryAdapterInfo
        {
            AdapterId = "eth0",
            Name = "Test",
            Icon = "🌐",
            DownloadSpeed = "0 B/s",
            UploadSpeed = "0 B/s",
            ColorHex = "#000"
        };

        info.DownloadBps.Should().Be(0);
        info.UploadBps.Should().Be(0);
    }

    [Test]
    public void SecondaryAdapterInfo_SpeedsAreMutable()
    {
        var info = new SecondaryAdapterInfo
        {
            AdapterId = "eth0",
            Name = "Test",
            Icon = "🌐",
            DownloadSpeed = "0 B/s",
            UploadSpeed = "0 B/s",
            ColorHex = "#000"
        };

        info.DownloadSpeed = "100 KB/s";
        info.UploadSpeed = "50 KB/s";
        info.DownloadBps = 100_000;
        info.UploadBps = 50_000;

        info.DownloadSpeed.Should().Be("100 KB/s");
        info.UploadSpeed.Should().Be("50 KB/s");
        info.DownloadBps.Should().Be(100_000);
        info.UploadBps.Should().Be(50_000);
    }

    #endregion

    #region NetworkMonitorConstants Tests

    [Test]
    public void AutoAdapterId_IsAutoString()
    {
        NetworkMonitorConstants.AutoAdapterId.Should().Be("auto");
    }



    #endregion

    #region AppSettings Default Tests

    [Test]
    public void AppSettings_DefaultSelectedAdapterId_IsAuto()
    {
        var settings = new AppSettings();
        settings.SelectedAdapterId.Should().Be(NetworkMonitorConstants.AutoAdapterId);
    }

    [Test]
    public void AppSettings_SelectedAdapterId_CanBeOverridden()
    {
        var settings = new AppSettings { SelectedAdapterId = "eth0" };
        settings.SelectedAdapterId.Should().Be("eth0");
    }

    [Test]
    public void AppSettings_SelectedAdapterId_CanBeEmptyForAggregateMode()
    {
        var settings = new AppSettings { SelectedAdapterId = string.Empty };
        settings.SelectedAdapterId.Should().BeEmpty();
    }

    #endregion

    #region NetworkStats Resolved Adapter Tests

    [Test]
    public void NetworkStats_ResolvedPrimaryAdapterId_DefaultsEmpty()
    {
        var stats = new NetworkStats();
        stats.ResolvedPrimaryAdapterId.Should().BeEmpty();
    }

    [Test]
    public void NetworkStats_ResolvedPrimaryAdapterName_DefaultsEmpty()
    {
        var stats = new NetworkStats();
        stats.ResolvedPrimaryAdapterName.Should().BeEmpty();
    }

    [Test]
    public void NetworkStats_ResolvedPrimaryAdapterId_CanBeSet()
    {
        var stats = new NetworkStats { ResolvedPrimaryAdapterId = "eth0" };
        stats.ResolvedPrimaryAdapterId.Should().Be("eth0");
    }

    [Test]
    public void NetworkStats_ResolvedPrimaryAdapterName_CanBeSet()
    {
        var stats = new NetworkStats { ResolvedPrimaryAdapterName = "Ethernet" };
        stats.ResolvedPrimaryAdapterName.Should().Be("Ethernet");
    }

    #endregion

    #region OverviewViewModel Auto Adapter Selection Tests

    [Test]
    public void LoadAdapters_AutoIsFirstItem()
    {
        var adapters = new List<NetworkAdapter>
        {
            new() { Id = "eth0", Name = "Ethernet", IsActive = true },
            new() { Id = "wifi0", Name = "WiFi", IsActive = true }
        };
        _networkMonitor.GetAdapters(Arg.Any<bool>()).Returns(adapters);

        var vm = CreateViewModel();

        vm.Adapters[0].IsAuto.Should().BeTrue();
        vm.Adapters[0].Id.Should().Be(NetworkMonitorConstants.AutoAdapterId);
    }

    [Test]
    public void LoadAdapters_AutoPlusRealAdapters()
    {
        var adapters = new List<NetworkAdapter>
        {
            new() { Id = "eth0", Name = "Ethernet", IsActive = true },
            new() { Id = "wifi0", Name = "WiFi", IsActive = true },
            new() { Id = "vpn0", Name = "VPN", IsActive = true }
        };
        _networkMonitor.GetAdapters(Arg.Any<bool>()).Returns(adapters);

        var vm = CreateViewModel();

        vm.Adapters.Should().HaveCount(4); // Auto + 3 real
    }

    [Test]
    public void LoadAdapters_WithNoActiveAdapters_OnlyAutoItem()
    {
        var adapters = new List<NetworkAdapter>
        {
            new() { Id = "eth0", Name = "Ethernet", IsActive = false }
        };
        _networkMonitor.GetAdapters(Arg.Any<bool>()).Returns(adapters);

        var vm = CreateViewModel();

        vm.Adapters.Should().HaveCount(1);
        vm.Adapters[0].IsAuto.Should().BeTrue();
    }

    [Test]
    public void LoadAdapters_WithEmptyList_OnlyAutoItem()
    {
        _networkMonitor.GetAdapters(Arg.Any<bool>()).Returns(new List<NetworkAdapter>());

        var vm = CreateViewModel();

        vm.Adapters.Should().HaveCount(1);
        vm.Adapters[0].IsAuto.Should().BeTrue();
    }

    [Test]
    public void SelectedAdapter_AutoCallsSetAdapterWithAutoId()
    {
        var adapters = new List<NetworkAdapter>
        {
            new() { Id = "eth0", Name = "Ethernet", IsActive = true }
        };
        _networkMonitor.GetAdapters(Arg.Any<bool>()).Returns(adapters);
        var vm = CreateViewModel();

        // Select a different adapter first so switching to Auto triggers the change handler
        vm.SelectedAdapter = vm.Adapters.First(a => a.Id == "eth0");
        _networkMonitor.ClearReceivedCalls();

        vm.SelectedAdapter = vm.Adapters.First(a => a.IsAuto);

        _networkMonitor.Received().SetAdapter(NetworkMonitorConstants.AutoAdapterId);
    }

    [Test]
    public void SelectedAdapter_RegularAdapterCallsSetAdapterWithId()
    {
        var adapters = new List<NetworkAdapter>
        {
            new() { Id = "eth0", Name = "Ethernet", IsActive = true }
        };
        _networkMonitor.GetAdapters(Arg.Any<bool>()).Returns(adapters);
        var vm = CreateViewModel();
        _networkMonitor.ClearReceivedCalls();

        vm.SelectedAdapter = vm.Adapters.First(a => a.Id == "eth0");

        _networkMonitor.Received().SetAdapter("eth0");
    }

    [Test]
    public void SelectedAdapter_NullFallsBackToAuto()
    {
        var adapters = new List<NetworkAdapter>
        {
            new() { Id = "eth0", Name = "Ethernet", IsActive = true }
        };
        _networkMonitor.GetAdapters(Arg.Any<bool>()).Returns(adapters);
        var vm = CreateViewModel();
        vm.SelectedAdapter = vm.Adapters.First(a => a.Id == "eth0");
        _networkMonitor.ClearReceivedCalls();

        vm.SelectedAdapter = null;

        _networkMonitor.Received(1).SetAdapter(NetworkMonitorConstants.AutoAdapterId);
    }

    #endregion

    #region OverviewViewModel Notification State Tests

    [Test]
    public void Constructor_NotificationIsHidden()
    {
        var vm = CreateViewModel();

        vm.AutoSwitchNotification.Should().BeEmpty();
        vm.IsAutoSwitchNotificationVisible.Should().BeFalse();
    }

    [Test]
    public void SecondaryAdapters_InitiallyEmpty()
    {
        var vm = CreateViewModel();

        vm.SecondaryAdapters.Should().NotBeNull();
        vm.SecondaryAdapters.Should().BeEmpty();
    }

    [Test]
    public void HasSecondaryAdapters_InitiallyFalse()
    {
        var vm = CreateViewModel();

        vm.HasSecondaryAdapters.Should().BeFalse();
    }

    #endregion

    #region OverviewViewModel RestoreSelectedAdapter Tests

    [Test]
    public async Task RestoreSelectedAdapter_WithAutoSetting_SelectsAutoItem()
    {
        var adapters = new List<NetworkAdapter>
        {
            new() { Id = "eth0", Name = "Ethernet", IsActive = true }
        };
        _networkMonitor.GetAdapters(Arg.Any<bool>()).Returns(adapters);
        _persistence.GetSettingsAsync().Returns(new AppSettings { SelectedAdapterId = "auto" });

        var vm = CreateViewModel();
        await vm.InitializationTask;

        vm.SelectedAdapter?.IsAuto.Should().BeTrue();
    }

    [Test]
    public async Task RestoreSelectedAdapter_WithSpecificAdapter_SelectsThatAdapter()
    {
        var adapters = new List<NetworkAdapter>
        {
            new() { Id = "eth0", Name = "Ethernet", IsActive = true }
        };
        _networkMonitor.GetAdapters(Arg.Any<bool>()).Returns(adapters);
        _persistence.GetSettingsAsync().Returns(new AppSettings { SelectedAdapterId = "eth0" });

        var vm = CreateViewModel();
        await vm.InitializationTask;

        vm.SelectedAdapter?.Id.Should().Be("eth0");
    }

    [Test]
    public async Task RestoreSelectedAdapter_WithNonExistentAdapter_FallsBackToFirst()
    {
        var adapters = new List<NetworkAdapter>
        {
            new() { Id = "eth0", Name = "Ethernet", IsActive = true }
        };
        _networkMonitor.GetAdapters(Arg.Any<bool>()).Returns(adapters);
        _persistence.GetSettingsAsync().Returns(new AppSettings { SelectedAdapterId = "nonexistent" });

        var vm = CreateViewModel();
        await vm.InitializationTask;

        // Falls back to first item (Auto)
        vm.SelectedAdapter?.IsAuto.Should().BeTrue();
    }

    #endregion

    #region OverviewViewModel Chart Series with Auto Tests

    [Test]
    public void ChartSeries_InitialCount_IsTwoForDownloadAndUpload()
    {
        var vm = CreateViewModel();

        vm.ChartSeries.Should().HaveCount(2);
    }

    [Test]
    public void ShowAdvancedAdapters_ReloadsAdaptersKeepingAuto()
    {
        var adapters = new List<NetworkAdapter>
        {
            new() { Id = "eth0", Name = "Ethernet", IsActive = true }
        };
        _networkMonitor.GetAdapters(Arg.Any<bool>()).Returns(adapters);
        var vm = CreateViewModel();

        // Add a virtual adapter for advanced mode
        var advancedAdapters = new List<NetworkAdapter>
        {
            new() { Id = "eth0", Name = "Ethernet", IsActive = true },
            new() { Id = "vm0", Name = "VMware", IsActive = true, IsVirtual = true }
        };
        _networkMonitor.GetAdapters(true).Returns(advancedAdapters);

        vm.ShowAdvancedAdapters = true;

        // Auto still first, plus all active adapters
        vm.Adapters[0].IsAuto.Should().BeTrue();
        vm.Adapters.Should().HaveCountGreaterThanOrEqualTo(2);
    }

    #endregion

    #region AdapterDisplayItem Edge Cases

    [Test]
    public void AutoAdapter_AdapterType_IsOther()
    {
        var item = AdapterDisplayItem.CreateAuto();
        item.AdapterType.Should().Be(NetworkAdapterType.Other);
    }

    [Test]
    public void AutoAdapter_IsNotVirtual()
    {
        var item = AdapterDisplayItem.CreateAuto();
        item.IsVirtual.Should().BeFalse();
    }

    [Test]
    public void AutoAdapter_IsNotVpn()
    {
        var item = AdapterDisplayItem.CreateAuto();
        item.IsKnownVpn.Should().BeFalse();
    }

    [Test]
    public void RegularAdapterWithAutoAsName_IsNotAutoItem()
    {
        // Ensure the "auto" detection is by ID, not name
        var adapter = new NetworkAdapter { Id = "real-id", Name = "Auto" };
        var item = new AdapterDisplayItem(adapter);

        item.IsAuto.Should().BeFalse();
    }

    #endregion

    public ValueTask DisposeAsync()
    {
        foreach (var vm in _createdViewModels)
        {
            vm.Dispose();
        }
        _createdViewModels.Clear();
        return ValueTask.CompletedTask;
    }
}
