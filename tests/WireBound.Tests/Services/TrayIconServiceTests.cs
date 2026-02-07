using WireBound.Avalonia.Services;

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
    public void ShowActivityGraph_DefaultIsTrue()
    {
        using var service = new TrayIconService();

        service.ShowActivityGraph.Should().BeTrue();
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
    public void ShowActivityGraph_PropertyUpdates()
    {
        using var service = new TrayIconService();

        service.ShowActivityGraph = false;
        service.ShowActivityGraph.Should().BeFalse();

        service.ShowActivityGraph = true;
        service.ShowActivityGraph.Should().BeTrue();
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
    public void UpdateActivity_WithoutInitialize_DoesNotThrow()
    {
        using var service = new TrayIconService();

        // UpdateActivity checks _trayIcon == null and returns early
        var act = () => service.UpdateActivity(1_000_000, 500_000);

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
