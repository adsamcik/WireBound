using System.Collections.ObjectModel;
using AwesomeAssertions;
using LiveChartsCore;
using LiveChartsCore.Defaults;
using LiveChartsCore.SkiaSharpView;
using SkiaSharp;
using WireBound.Avalonia.Helpers;
using WireBound.Tests.Fixtures;

namespace WireBound.Tests.Helpers;

/// <summary>
/// Unit tests for ChartSeriesFactory helper class.
/// Verifies that all factory methods return correctly structured, non-null results.
/// </summary>
public class ChartSeriesFactoryTests
{
    public ChartSeriesFactoryTests()
    {
        LiveChartsHook.EnsureInitialized();
    }

    // ÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉ
    // SERIES CREATION
    // ÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉ

    [Test]
    public void CreateSpeedLineSeries_WithValidPoints_ReturnsTwoSeries()
    {
        // Arrange
        var downloadPoints = new ObservableCollection<DateTimePoint>();
        var uploadPoints = new ObservableCollection<DateTimePoint>();

        // Act
        var result = ChartSeriesFactory.CreateSpeedLineSeries(downloadPoints, uploadPoints);

        // Assert
        result.Should().NotBeNull();
        result.Should().HaveCount(2);
        result[0].Name.Should().Be("Download");
        result[1].Name.Should().Be("Upload");
    }

    [Test]
    public void CreateOverlayLineSeries_WithValidParameters_ReturnsSeriesWithCorrectName()
    {
        // Arrange
        var points = new ObservableCollection<DateTimePoint>();
        var color = SKColors.Blue;
        var name = "CPU Usage";

        // Act
        var result = ChartSeriesFactory.CreateOverlayLineSeries(name, points, color);

        // Assert
        result.Should().NotBeNull();
        result.Name.Should().Be("CPU Usage");
    }

    // ÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉ
    // AXIS CREATION
    // ÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉ

    [Test]
    public void CreateTimeXAxes_ReturnsNonEmptyArray()
    {
        // Act
        var result = ChartSeriesFactory.CreateTimeXAxes();

        // Assert
        result.Should().NotBeNull();
        result.Should().NotBeEmpty();
    }

    [Test]
    public void CreateSpeedYAxes_ReturnsNonEmptyArray()
    {
        // Act
        var result = ChartSeriesFactory.CreateSpeedYAxes();

        // Assert
        result.Should().NotBeNull();
        result.Should().NotBeEmpty();
    }

    [Test]
    public void CreateUsageYAxes_ReturnsNonEmptyArray()
    {
        // Act
        var result = ChartSeriesFactory.CreateUsageYAxes();

        // Assert
        result.Should().NotBeNull();
        result.Should().NotBeEmpty();
    }

    [Test]
    public void CreatePercentageYAxes_ReturnsNonEmptyArray()
    {
        // Act
        var result = ChartSeriesFactory.CreatePercentageYAxes();

        // Assert
        result.Should().NotBeNull();
        result.Should().NotBeEmpty();
    }

    // ÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉ
    // COLUMN SERIES
    // ÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉ

    [Test]
    public void CreateUsageColumnSeries_WithValidValues_ReturnsNonNull()
    {
        // Arrange
        IReadOnlyCollection<long> downloadValues = [100L, 200L, 300L];
        IReadOnlyCollection<long> uploadValues = [50L, 100L, 150L];

        // Act
        var result = ChartSeriesFactory.CreateUsageColumnSeries(downloadValues, uploadValues);

        // Assert
        result.Should().NotBeNull();
        result.Should().HaveCount(2);
        result[0].Name.Should().Be("Download");
        result[1].Name.Should().Be("Upload");
    }

    [Test]
    public void CreateHourlyColumnSeries_WithValidValues_ReturnsNonNull()
    {
        // Arrange
        IReadOnlyCollection<long> downloadValues = [100L, 200L, 300L];
        IReadOnlyCollection<long> uploadValues = [50L, 100L, 150L];

        // Act
        var result = ChartSeriesFactory.CreateHourlyColumnSeries(downloadValues, uploadValues);

        // Assert
        result.Should().NotBeNull();
        result.Should().HaveCount(2);
        result[0].Name.Should().Be("Download");
        result[1].Name.Should().Be("Upload");
    }

    // ÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉ
    // SPARKLINE
    // ÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉ

    [Test]
    public void CreateSparklineSeries_WithDownloadFlag_ReturnsNonNull()
    {
        // Arrange
        var points = new ObservableCollection<DateTimePoint>();

        // Act
        var result = ChartSeriesFactory.CreateSparklineSeries(points, isDownload: true);

        // Assert
        result.Should().NotBeNull();
        result.Should().NotBeEmpty();
    }

    [Test]
    public void CreateSparklineAxes_ForXAxis_ReturnsNonEmptyArray()
    {
        // Act
        var xAxes = ChartSeriesFactory.CreateSparklineAxes(isXAxis: true);

        // Assert
        xAxes.Should().NotBeNull();
        xAxes.Should().NotBeEmpty();
    }

    [Test]
    public void CreateSparklineAxes_ForYAxis_ReturnsNonEmptyArray()
    {
        // Act
        var yAxes = ChartSeriesFactory.CreateSparklineAxes(isXAxis: false);

        // Assert
        yAxes.Should().NotBeNull();
        yAxes.Should().NotBeEmpty();
    }
}
