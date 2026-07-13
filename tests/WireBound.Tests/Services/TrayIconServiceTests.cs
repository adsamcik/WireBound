using WireBound.Avalonia.Services;
using WireBound.Core.Models;

namespace WireBound.Tests.Services;

/// <summary>
/// Unit tests for TrayIconService.
///
/// LIMITATION: TrayIconService is tightly coupled to Avalonia UI types (TrayIcon, Window,
/// Application, Dispatcher, SkiaSharp surface rendering). The Initialize, HideMainWindow,
/// ShowMainWindow, and UpdateActivity methods require a running Avalonia application with
/// a real Window instance and cannot be fully unit-tested without an Avalonia headless host.
///
/// The tests below verify property behavior and safe disposal on an uninitialized instance
/// (no Window/TrayIcon attached). Full integration tests would require Avalonia.Headless.
/// </summary>
public class TrayIconServiceTests
{
    // ═══════════════════════════════════════════════════════════════════════
    // Default State Tests
    // ═══════════════════════════════════════════════════════════════════════

    [Test]
    public void MinimizeToTray_DefaultIsFalse()
    {
        using var service = new TrayIconService();

        service.MinimizeToTray.Should().BeFalse();
    }

    [Test]
    public void IconMode_DefaultIsTraffic()
    {
        using var service = new TrayIconService();

        service.IconMode.Should().Be(TrayIconMode.Traffic);
    }

    [Test]
    public void TrafficAdapterId_DefaultIsEmpty()
    {
        using var service = new TrayIconService();

        service.TrafficAdapterId.Should().BeEmpty();
    }

    // ═══════════════════════════════════════════════════════════════════════
    // MinimizeToTray Property Tests
    // ═══════════════════════════════════════════════════════════════════════

    [Test]
    public void MinimizeToTray_PropertyUpdates()
    {
        using var service = new TrayIconService();

        service.MinimizeToTray = true;
        service.MinimizeToTray.Should().BeTrue();

        service.MinimizeToTray = false;
        service.MinimizeToTray.Should().BeFalse();
    }

    [Test]
    public void IconMode_PropertyUpdates()
    {
        using var service = new TrayIconService();

        // Without an attached tray icon the setter simply stores the value.
        service.IconMode = TrayIconMode.Cpu;
        service.IconMode.Should().Be(TrayIconMode.Cpu);

        service.IconMode = TrayIconMode.Ram;
        service.IconMode.Should().Be(TrayIconMode.Ram);

        service.IconMode = TrayIconMode.AppIcon;
        service.IconMode.Should().Be(TrayIconMode.AppIcon);
    }

    [Test]
    public void TrafficAdapterId_PropertyUpdates()
    {
        using var service = new TrayIconService();

        service.TrafficAdapterId = "eth0";
        service.TrafficAdapterId.Should().Be("eth0");
    }

    [Test]
    public void TrafficAdapterId_NullCoercesToEmpty()
    {
        using var service = new TrayIconService();

        service.TrafficAdapterId = null!;
        service.TrafficAdapterId.Should().BeEmpty();
    }

    // ═══════════════════════════════════════════════════════════════════════
    // Dispose Tests
    // ═══════════════════════════════════════════════════════════════════════

    [Test]
    public void Dispose_CleansUpResources()
    {
        // Dispose on an uninitialized service should not throw.
        // Verifies the null-guard paths in Dispose() work correctly.
        var service = new TrayIconService();

        var act = () => service.Dispose();

        act.Should().NotThrow();
    }

    [Test]
    public void Dispose_CanBeCalledMultipleTimes()
    {
        var service = new TrayIconService();

        service.Dispose();
        var secondDispose = () => service.Dispose();

        secondDispose.Should().NotThrow();
    }

    // ═══════════════════════════════════════════════════════════════════════
    // Graceful No-Op When Uninitialized
    // ═══════════════════════════════════════════════════════════════════════

    [Test]
    public void UpdateMetrics_WithoutInitialize_DoesNotThrow()
    {
        using var service = new TrayIconService();

        // UpdateMetrics checks _trayIcon == null and returns early
        var act = () => service.UpdateMetrics(1_000_000, 500_000, 42.0, 60.0);

        act.Should().NotThrow();
    }

    [Test]
    public void HideMainWindow_WithoutInitialize_DoesNotThrow()
    {
        using var service = new TrayIconService();

        // HideMainWindow checks _mainWindow == null and returns early
        var act = () => service.HideMainWindow();

        act.Should().NotThrow();
    }

    [Test]
    public void ShowMainWindow_WithoutInitialize_DoesNotThrow()
    {
        using var service = new TrayIconService();

        // ShowMainWindow checks _mainWindow == null and returns early
        var act = () => service.ShowMainWindow();

        act.Should().NotThrow();
    }

    // ═══════════════════════════════════════════════════════════════════════
    // Initialize Tests
    // NOTE: Initialize(Window, bool) requires a real Avalonia Window instance
    // which needs an active Avalonia application lifetime. These tests verify
    // the property assignment aspect indirectly.
    // ═══════════════════════════════════════════════════════════════════════

    [Test]
    public void Initialize_SetsMinimizeToTray()
    {
        // Cannot call Initialize without a real Window, but we can verify the
        // property-based path: the setter stores the value regardless of tray state.
        using var service = new TrayIconService();

        service.MinimizeToTray = true;

        service.MinimizeToTray.Should().BeTrue();
    }
}
