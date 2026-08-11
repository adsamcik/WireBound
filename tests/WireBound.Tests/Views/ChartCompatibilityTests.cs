using LiveChartsCore.SkiaSharpView.Avalonia;
using WireBound.Tests.Fixtures;

namespace WireBound.Tests.Views;

/// <summary>
/// Guards the Avalonia and LiveCharts binary compatibility required during startup.
/// </summary>
public class ChartCompatibilityTests
{
    [Test]
    public void CartesianChart_CurrentAvaloniaVersion_ConstructsSuccessfully()
    {
        LiveChartsHook.EnsureInitialized();

        var action = () => _ = new CartesianChart();

        action.Should().NotThrow(
            "the dashboard constructs a CartesianChart during startup, so Avalonia and LiveCharts must be binary compatible");
    }
}
