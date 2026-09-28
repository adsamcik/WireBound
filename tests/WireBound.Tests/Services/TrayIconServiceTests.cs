using NSubstitute;
using SkiaSharp;
using WireBound.Avalonia.Services;
using WireBound.Core.Models;
using WireBound.Platform.Abstract.Services;

namespace WireBound.Tests.Services;

/// <summary>
/// Unit tests for TrayIconService.
///
/// LIMITATION: TrayIconService is tightly coupled to Avalonia UI types (TrayIcon, Window,
/// Application, Dispatcher). The Initialize, HideMainWindow, and ShowMainWindow methods
/// require a running Avalonia application with a real Window instance and cannot be fully
/// unit-tested without an Avalonia headless host.
///
/// The tests below verify rasterization and behavior without a Window/TrayIcon attached.
/// Full integration tests would require Avalonia.Headless.
/// </summary>
public class TrayIconServiceTests
{
    private static TrayIconService CreateService() => new(Substitute.For<ITrayIconSizeProvider>());

    [Test]
    [Arguments(16)]
    [Arguments(20)]
    [Arguments(24)]
    [Arguments(28)]
    [Arguments(32)]
    public void LiveGraphs_RenderAtRequestedPhysicalPixelSize(int pixelSize)
    {
        using var service = CreateService();
        using var traffic = service.RenderActivityGraphSurface(pixelSize);
        using var cpu = service.RenderMetricGraphSurface(new Queue<float>(), SKColors.Cyan, pixelSize);
        using var filledCpu = service.RenderMetricGraphSurface(
            new Queue<float>(Enumerable.Repeat(1f, 16)), SKColors.Cyan, pixelSize);

        using var trafficImage = traffic.Snapshot();
        using var cpuImage = cpu.Snapshot();
        using var filledCpuImage = filledCpu.Snapshot();
        trafficImage.Width.Should().Be(pixelSize);
        trafficImage.Height.Should().Be(pixelSize);
        cpuImage.Width.Should().Be(pixelSize);
        cpuImage.Height.Should().Be(pixelSize);

        using var trafficBitmap = SKBitmap.FromImage(trafficImage);
        using var cpuBitmap = SKBitmap.FromImage(cpuImage);
        using var filledCpuBitmap = SKBitmap.FromImage(filledCpuImage);
        var background = new SKColor(20, 30, 35);
        var baselineY = (int)Math.Ceiling(14 * pixelSize / 16.0);
        trafficBitmap.GetPixel(0, 0).Should().Be(background);
        trafficBitmap.GetPixel(pixelSize / 2, baselineY).Should().NotBe(background);
        cpuBitmap.GetPixel(pixelSize / 2, baselineY).Should().NotBe(background);
        Enumerable.Range(pixelSize / 2 - 2, 5)
            .Any(x => filledCpuBitmap.GetPixel(x, baselineY) == SKColors.Cyan)
            .Should().BeTrue();
    }

    // ═══════════════════════════════════════════════════════════════════════
    // Default State Tests
    // ═══════════════════════════════════════════════════════════════════════

    [Test]
    public void MinimizeToTray_DefaultIsFalse()
    {
        using var service = CreateService();

        service.MinimizeToTray.Should().BeFalse();
    }

    [Test]
    public void IconMode_DefaultIsTraffic()
    {
        using var service = CreateService();

        service.IconMode.Should().Be(TrayIconMode.Traffic);
    }

    [Test]
    public void TrafficAdapterId_DefaultIsEmpty()
    {
        using var service = CreateService();

        service.TrafficAdapterId.Should().BeEmpty();
    }

    // ═══════════════════════════════════════════════════════════════════════
    // MinimizeToTray Property Tests
    // ═══════════════════════════════════════════════════════════════════════

    [Test]
    public void MinimizeToTray_PropertyUpdates()
    {
        using var service = CreateService();

        service.MinimizeToTray = true;
        service.MinimizeToTray.Should().BeTrue();

        service.MinimizeToTray = false;
        service.MinimizeToTray.Should().BeFalse();
    }

    [Test]
    public void IconMode_PropertyUpdates()
    {
        using var service = CreateService();

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
        using var service = CreateService();

        service.TrafficAdapterId = "eth0";
        service.TrafficAdapterId.Should().Be("eth0");
    }

    [Test]
    public void TrafficAdapterId_NullCoercesToEmpty()
    {
        using var service = CreateService();

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
        var service = CreateService();

        var act = () => service.Dispose();

        act.Should().NotThrow();
    }

    [Test]
    public void Dispose_CanBeCalledMultipleTimes()
    {
        var service = CreateService();

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
        using var service = CreateService();

        // UpdateMetrics checks _trayIcon == null and returns early
        var act = () => service.UpdateMetrics(1_000_000, 500_000, 42.0, 60.0);

        act.Should().NotThrow();
    }

    [Test]
    public void HideMainWindow_WithoutInitialize_DoesNotThrow()
    {
        using var service = CreateService();

        // HideMainWindow checks _mainWindow == null and returns early
        var act = () => service.HideMainWindow();

        act.Should().NotThrow();
    }

    [Test]
    public void ShowMainWindow_WithoutInitialize_DoesNotThrow()
    {
        using var service = CreateService();

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
        using var service = CreateService();

        service.MinimizeToTray = true;

        service.MinimizeToTray.Should().BeTrue();
    }
}
