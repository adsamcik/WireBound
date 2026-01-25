using AwesomeAssertions;
using LiveChartsCore.Defaults;
using WireBound.Core.Helpers;
using WireBound.Tests.Fixtures;

namespace WireBound.Tests.Helpers;

/// <summary>
/// Unit tests for LTTB (Largest-Triangle-Three-Buckets) downsampling algorithm
/// </summary>
[Collection("LiveCharts")]
public class LttbDownsamplerTests
{
    // ═══════════════════════════════════════════════════════════════════════
    // Edge Cases and Empty Input Tests
    // ═══════════════════════════════════════════════════════════════════════

    [Fact]
    public void Downsample_NullInput_ReturnsEmptyList()
    {
        // Act
        var result = LttbDownsampler.Downsample(null!, 10);

        // Assert
        result.Should().BeEmpty();
    }

    [Fact]
    public void Downsample_EmptyInput_ReturnsEmptyList()
    {
        // Arrange
        var data = new List<DateTimePoint>();

        // Act
        var result = LttbDownsampler.Downsample(data, 10);

        // Assert
        result.Should().BeEmpty();
    }

    [Fact]
    public void Downsample_SinglePoint_ReturnsSamePoint()
    {
        // Arrange
        var data = new List<DateTimePoint>
        {
            new(DateTime.Now, 100)
        };

        // Act
        var result = LttbDownsampler.Downsample(data, 10);

        // Assert
        result.Should().HaveCount(1);
        result[0].Value.Should().Be(100);
    }

    // ═══════════════════════════════════════════════════════════════════════
    // Pass-through Cases (No Downsampling Needed)
    // ═══════════════════════════════════════════════════════════════════════

    [Fact]
    public void Downsample_DataSmallerThanTarget_ReturnsAllPoints()
    {
        // Arrange
        var baseTime = DateTime.Now;
        var data = Enumerable.Range(0, 5)
            .Select(i => new DateTimePoint(baseTime.AddSeconds(i), i * 10))
            .ToList();

        // Act
        var result = LttbDownsampler.Downsample(data, 10);

        // Assert
        result.Should().HaveCount(5);
    }

    [Fact]
    public void Downsample_DataEqualsTarget_ReturnsAllPoints()
    {
        // Arrange
        var baseTime = DateTime.Now;
        var data = Enumerable.Range(0, 10)
            .Select(i => new DateTimePoint(baseTime.AddSeconds(i), i * 10))
            .ToList();

        // Act
        var result = LttbDownsampler.Downsample(data, 10);

        // Assert
        result.Should().HaveCount(10);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    [InlineData(2)]
    public void Downsample_TargetLessThan3_ReturnsAllPoints(int target)
    {
        // Arrange
        var baseTime = DateTime.Now;
        var data = Enumerable.Range(0, 100)
            .Select(i => new DateTimePoint(baseTime.AddSeconds(i), i))
            .ToList();

        // Act
        var result = LttbDownsampler.Downsample(data, target);

        // Assert
        result.Should().HaveCount(100); // Returns all when target < 3
    }

    // ═══════════════════════════════════════════════════════════════════════
    // Basic Downsampling Tests
    // ═══════════════════════════════════════════════════════════════════════

    [Fact]
    public void Downsample_BasicReduction_ReducesToTargetCount()
    {
        // Arrange
        var baseTime = DateTime.Now;
        var data = Enumerable.Range(0, 100)
            .Select(i => new DateTimePoint(baseTime.AddSeconds(i), i))
            .ToList();

        // Act
        var result = LttbDownsampler.Downsample(data, 20);

        // Assert
        result.Should().HaveCount(20);
    }

    [Fact]
    public void Downsample_PreservesFirstAndLastPoints()
    {
        // Arrange
        var baseTime = DateTime.Now;
        var data = Enumerable.Range(0, 100)
            .Select(i => new DateTimePoint(baseTime.AddSeconds(i), i * 10))
            .ToList();

        // Act
        var result = LttbDownsampler.Downsample(data, 10);

        // Assert
        // First point should match
        result.First().DateTime.Should().Be(data.First().DateTime);
        result.First().Value.Should().Be(data.First().Value);

        // Last point should match
        result.Last().DateTime.Should().Be(data.Last().DateTime);
        result.Last().Value.Should().Be(data.Last().Value);
    }

    [Fact]
    public void Downsample_MaintainsChronologicalOrder()
    {
        // Arrange
        var baseTime = DateTime.Now;
        var data = Enumerable.Range(0, 100)
            .Select(i => new DateTimePoint(baseTime.AddSeconds(i), Math.Sin(i * 0.1) * 100))
            .ToList();

        // Act
        var result = LttbDownsampler.Downsample(data, 20);

        // Assert
        for (int i = 1; i < result.Count; i++)
        {
            result[i].DateTime.Should().BeAfter(result[i - 1].DateTime);
        }
    }

    // ═══════════════════════════════════════════════════════════════════════
    // Peak and Valley Preservation Tests
    // ═══════════════════════════════════════════════════════════════════════

    [Fact]
    public void Downsample_PreservesPeakValues()
    {
        // Arrange - Create data with a clear peak
        var baseTime = DateTime.Now;
        var data = new List<DateTimePoint>();

        // Build up to peak
        for (int i = 0; i < 50; i++)
        {
            data.Add(new DateTimePoint(baseTime.AddSeconds(i), i * 2));
        }

        // Peak at index 50
        data.Add(new DateTimePoint(baseTime.AddSeconds(50), 1000));

        // Fall back down
        for (int i = 51; i < 100; i++)
        {
            data.Add(new DateTimePoint(baseTime.AddSeconds(i), (100 - i) * 2));
        }

        // Act
        var result = LttbDownsampler.Downsample(data, 20);

        // Assert - The peak value should be preserved
        result.Max(p => p.Value ?? 0).Should().Be(1000);
    }

    [Fact]
    public void Downsample_PreservesValleyValues()
    {
        // Arrange - Create data with a clear valley
        var baseTime = DateTime.Now;
        var data = new List<DateTimePoint>();

        // Start high
        for (int i = 0; i < 50; i++)
        {
            data.Add(new DateTimePoint(baseTime.AddSeconds(i), 100 - i));
        }

        // Valley at index 50
        data.Add(new DateTimePoint(baseTime.AddSeconds(50), -100));

        // Rise back up
        for (int i = 51; i < 100; i++)
        {
            data.Add(new DateTimePoint(baseTime.AddSeconds(i), i - 50));
        }

        // Act
        var result = LttbDownsampler.Downsample(data, 20);

        // Assert - The valley value should be preserved
        result.Min(p => p.Value ?? 0).Should().Be(-100);
    }

    [Fact]
    public void Downsample_SineWave_PreservesWaveform()
    {
        // Arrange - Create a sine wave
        var baseTime = DateTime.Now;
        var data = Enumerable.Range(0, 360)
            .Select(i => new DateTimePoint(
                baseTime.AddSeconds(i),
                Math.Sin(i * Math.PI / 180) * 100))
            .ToList();

        // Act
        var result = LttbDownsampler.Downsample(data, 36);

        // Assert
        // Original max should be ~100, min should be ~-100
        var originalMax = data.Max(p => p.Value ?? 0);
        var originalMin = data.Min(p => p.Value ?? 0);

        var resultMax = result.Max(p => p.Value ?? 0);
        var resultMin = result.Min(p => p.Value ?? 0);

        // Downsampled should preserve extremes with some tolerance
        resultMax.Should().BeGreaterThan(originalMax * 0.9);
        resultMin.Should().BeLessThan(originalMin * 0.9);
    }

    // ═══════════════════════════════════════════════════════════════════════
    // Null Value Handling Tests
    // ═══════════════════════════════════════════════════════════════════════

    [Fact]
    public void Downsample_NullValues_TreatedAsZero()
    {
        // Arrange
        var baseTime = DateTime.Now;
        var data = new List<DateTimePoint>
        {
            new(baseTime, 100),
            new(baseTime.AddSeconds(1), null),
            new(baseTime.AddSeconds(2), null),
            new(baseTime.AddSeconds(3), 200),
            new(baseTime.AddSeconds(4), null),
            new(baseTime.AddSeconds(5), 300)
        };

        // Act
        var result = LttbDownsampler.Downsample(data, 4);

        // Assert
        result.Should().HaveCount(4);
        // First and last should be preserved
        result.First().Value.Should().Be(100);
        result.Last().Value.Should().Be(300);
    }

    // ═══════════════════════════════════════════════════════════════════════
    // Performance and Scale Tests
    // ═══════════════════════════════════════════════════════════════════════

    [Fact]
    public void Downsample_LargeDataset_CompletesInReasonableTime()
    {
        // Arrange
        var baseTime = DateTime.Now;
        var data = Enumerable.Range(0, 100_000)
            .Select(i => new DateTimePoint(baseTime.AddMilliseconds(i), Math.Sin(i * 0.01) * 100))
            .ToList();

        // Act
        var sw = System.Diagnostics.Stopwatch.StartNew();
        var result = LttbDownsampler.Downsample(data, 1000);
        sw.Stop();

        // Assert
        result.Should().HaveCount(1000);
        sw.ElapsedMilliseconds.Should().BeLessThan(1000); // Should complete in under 1 second
    }

    [Theory]
    [InlineData(1000, 100)]
    [InlineData(1000, 500)]
    [InlineData(10000, 100)]
    [InlineData(10000, 1000)]
    public void Downsample_VariousScales_ProducesCorrectCount(int dataPoints, int targetPoints)
    {
        // Arrange
        var baseTime = DateTime.Now;
        var data = Enumerable.Range(0, dataPoints)
            .Select(i => new DateTimePoint(baseTime.AddSeconds(i), i))
            .ToList();

        // Act
        var result = LttbDownsampler.Downsample(data, targetPoints);

        // Assert
        result.Should().HaveCount(targetPoints);
    }

    // ═══════════════════════════════════════════════════════════════════════
    // Data Pattern Tests
    // ═══════════════════════════════════════════════════════════════════════

    [Fact]
    public void Downsample_ConstantValues_Works()
    {
        // Arrange - All same value
        var baseTime = DateTime.Now;
        var data = Enumerable.Range(0, 100)
            .Select(i => new DateTimePoint(baseTime.AddSeconds(i), 50))
            .ToList();

        // Act
        var result = LttbDownsampler.Downsample(data, 10);

        // Assert
        result.Should().HaveCount(10);
        result.All(p => p.Value == 50).Should().BeTrue();
    }

    [Fact]
    public void Downsample_LinearIncreasing_PreservesEndpoints()
    {
        // Arrange
        var baseTime = DateTime.Now;
        var data = Enumerable.Range(0, 100)
            .Select(i => new DateTimePoint(baseTime.AddSeconds(i), i))
            .ToList();

        // Act
        var result = LttbDownsampler.Downsample(data, 10);

        // Assert
        result.First().Value.Should().Be(0);
        result.Last().Value.Should().Be(99);
    }

    [Fact]
    public void Downsample_StepFunction_PreservesSteps()
    {
        // Arrange - Step function: 0,0,0...100,100,100...0,0,0
        var baseTime = DateTime.Now;
        var data = new List<DateTimePoint>();

        for (int i = 0; i < 30; i++)
            data.Add(new DateTimePoint(baseTime.AddSeconds(i), 0));
        for (int i = 30; i < 60; i++)
            data.Add(new DateTimePoint(baseTime.AddSeconds(i), 100));
        for (int i = 60; i < 90; i++)
            data.Add(new DateTimePoint(baseTime.AddSeconds(i), 0));

        // Act
        var result = LttbDownsampler.Downsample(data, 15);

        // Assert - Should preserve the step transitions
        var distinctValues = result.Select(p => p.Value).Distinct().ToList();
        distinctValues.Should().Contain(0);
        distinctValues.Should().Contain(100);
    }

    [Fact]
    public void Downsample_SpikyData_PreservesSpikes()
    {
        // Arrange - Mostly flat with occasional spikes
        var baseTime = DateTime.Now;
        var data = new List<DateTimePoint>();

        for (int i = 0; i < 100; i++)
        {
            var value = (i % 25 == 0) ? 1000 : 10; // Spike every 25 points
            data.Add(new DateTimePoint(baseTime.AddSeconds(i), value));
        }

        // Act
        var result = LttbDownsampler.Downsample(data, 20);

        // Assert - Should preserve at least some spikes
        result.Any(p => p.Value >= 1000).Should().BeTrue();
    }
}
