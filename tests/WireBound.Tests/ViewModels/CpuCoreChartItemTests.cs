using AwesomeAssertions;
using WireBound.Avalonia.ViewModels;

namespace WireBound.Tests.ViewModels;

public class CpuCoreChartItemTests
{
    [Test]
    public void AddSample_UpdatesCurrentValuesAndHistories()
    {
        var core = new CpuCoreChartItem(3);

        core.AddSample(72.4, 18.6);

        core.Label.Should().Be("CPU 3");
        core.UsagePercent.Should().Be(72.4);
        core.KernelPercent.Should().Be(18.6);
        core.UsageFormatted.Should().Be("72%");
        core.KernelFormatted.Should().Be("19% kernel");
        core.UsageHistory.Should().ContainSingle().Which.Should().Be(72.4);
        core.KernelHistory.Should().ContainSingle().Which.Should().Be(18.6);
    }

    [Test]
    public void AddSample_ClampsInvalidValuesAndKernelToTotalUsage()
    {
        var core = new CpuCoreChartItem(0);

        core.AddSample(25, 80);
        core.AddSample(double.NaN, double.PositiveInfinity);

        core.UsageHistory.Should().BeEquivalentTo([25, 0], options => options.WithStrictOrdering());
        core.KernelHistory.Should().BeEquivalentTo([25, 0], options => options.WithStrictOrdering());
    }

    [Test]
    public void AddSample_RetainsOnlyOneMinuteOfHistory()
    {
        var core = new CpuCoreChartItem(0);

        for (var i = 0; i < 75; i++)
        {
            core.AddSample(i, i / 2d);
        }

        core.UsageHistory.Should().HaveCount(60);
        core.KernelHistory.Should().HaveCount(60);
        core.UsageHistory[0].Should().Be(15);
        core.UsageHistory[^1].Should().Be(74);
    }
}
