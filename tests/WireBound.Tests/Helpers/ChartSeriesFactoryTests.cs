using System.Collections.ObjectModel;
using AwesomeAssertions;
using LiveChartsCore;
using LiveChartsCore.Defaults;
using LiveChartsCore.Measure;
using LiveChartsCore.SkiaSharpView;
using LiveChartsCore.SkiaSharpView.Painting;
using LiveChartsCore.SkiaSharpView.Painting.Effects;
using SkiaSharp;
using WireBound.Avalonia.Helpers;
using WireBound.Core.Helpers;
using WireBound.Tests.Fixtures;

namespace WireBound.Tests.Helpers;

/// <summary>
/// Unit tests for ChartSeriesFactory helper class.
/// Verifies that all factory methods return correctly configured series and axes.
/// </summary>
public class ChartSeriesFactoryTests
{
    public ChartSeriesFactoryTests()
    {
        LiveChartsHook.EnsureInitialized();
    }

    [Test]
    public void CreateSpeedLineSeries_WithValidPoints_ReturnsTwoSeries()
    {
        // Arrange
        var downloadPoints = new ObservableCollection<DateTimePoint>();
        var uploadPoints = new ObservableCollection<DateTimePoint>();

        // Act
        var result = ChartSeriesFactory.CreateSpeedLineSeries(downloadPoints, uploadPoints);

        // Assert
        result.Should().HaveCount(2);
        result[0].Name.Should().Be("Download");
        result[1].Name.Should().Be("Upload");
    }

    [Test]
    public void CreateSpeedLineSeries_DownloadSeries_HasCorrectConfiguration()
    {
        // Arrange
        var downloadPoints = new ObservableCollection<DateTimePoint>();
        var uploadPoints = new ObservableCollection<DateTimePoint>();

        // Act
        var result = ChartSeriesFactory.CreateSpeedLineSeries(downloadPoints, uploadPoints);
        var download = result[0].Should().BeOfType<LineSeries<DateTimePoint>>().Value;

        // Assert
        download.Values.Should().BeSameAs(downloadPoints);
        download.Stroke.Should().BeOfType<SolidColorPaint>()
            .Which.Color.Should().Be(ChartColors.DownloadAccentColor);
        ((SolidColorPaint)download.Stroke!).StrokeThickness.Should().Be(2);
        download.Fill.Should().BeOfType<LinearGradientPaint>();
        download.GeometryFill.Should().BeNull();
        download.GeometryStroke.Should().BeNull();
        download.LineSmoothness.Should().Be(1);
        download.AnimationsSpeed.Should().Be(TimeSpan.Zero);
    }

    [Test]
    public void CreateSpeedLineSeries_UploadSeries_HasCorrectConfiguration()
    {
        // Arrange
        var downloadPoints = new ObservableCollection<DateTimePoint>();
        var uploadPoints = new ObservableCollection<DateTimePoint>();

        // Act
        var result = ChartSeriesFactory.CreateSpeedLineSeries(downloadPoints, uploadPoints);
        var upload = result[1].Should().BeOfType<LineSeries<DateTimePoint>>().Value;

        // Assert
        upload.Values.Should().BeSameAs(uploadPoints);
        upload.Stroke.Should().BeOfType<SolidColorPaint>()
            .Which.Color.Should().Be(ChartColors.UploadAccentColor);
        ((SolidColorPaint)upload.Stroke!).StrokeThickness.Should().Be(2);
        upload.Fill.Should().BeOfType<LinearGradientPaint>();
        upload.GeometryFill.Should().BeNull();
        upload.GeometryStroke.Should().BeNull();
        upload.LineSmoothness.Should().Be(1);
        upload.AnimationsSpeed.Should().Be(TimeSpan.Zero);
    }

    [Test]
    public void CreateOverlayLineSeries_WithDashedLine_HasCorrectConfiguration()
    {
        // Arrange
        var points = new ObservableCollection<DateTimePoint>();
        var color = SKColors.Blue;

        // Act
        var result = ChartSeriesFactory.CreateOverlayLineSeries("CPU Usage", points, color);

        // Assert
        result.Name.Should().Be("CPU Usage");
        result.Values.Should().BeSameAs(points);
        result.Fill.Should().BeNull();
        result.GeometryFill.Should().BeNull();
        result.GeometryStroke.Should().BeNull();
        result.LineSmoothness.Should().Be(0.5);
        result.AnimationsSpeed.Should().Be(TimeSpan.Zero);
        result.ScalesYAt.Should().Be(1);

        var stroke = result.Stroke.Should().BeOfType<SolidColorPaint>().Value;
        stroke.Color.Should().Be(SKColors.Blue);
        stroke.StrokeThickness.Should().Be(2);
        stroke.PathEffect.Should().BeOfType<DashEffect>();
    }

    [Test]
    public void CreateOverlayLineSeries_WithoutDashedLine_HasSolidStroke()
    {
        // Arrange
        var points = new ObservableCollection<DateTimePoint>();
        var color = SKColors.Red;

        // Act
        var result = ChartSeriesFactory.CreateOverlayLineSeries("Memory", points, color, useDashedLine: false);

        // Assert
        result.Name.Should().Be("Memory");
        var stroke = result.Stroke.Should().BeOfType<SolidColorPaint>().Value;
        stroke.Color.Should().Be(SKColors.Red);
        stroke.PathEffect.Should().BeNull();
    }

    [Test]
    public void CreateAdapterOverlayLineSeries_NonVpn_HasSolidThinStroke()
    {
        // Arrange
        var points = new ObservableCollection<DateTimePoint>();
        var color = SKColors.Green;

        // Act
        var result = ChartSeriesFactory.CreateAdapterOverlayLineSeries("Ethernet", points, color);

        // Assert
        result.Name.Should().Be("Ethernet");
        result.Values.Should().BeSameAs(points);
        result.Fill.Should().BeNull();
        result.GeometryFill.Should().BeNull();
        result.GeometryStroke.Should().BeNull();
        result.LineSmoothness.Should().Be(0.8);
        result.AnimationsSpeed.Should().Be(TimeSpan.Zero);
        result.ScalesYAt.Should().Be(0);

        var stroke = result.Stroke.Should().BeOfType<SolidColorPaint>().Value;
        stroke.Color.Should().Be(SKColors.Green);
        stroke.StrokeThickness.Should().Be(1.5f);
        stroke.PathEffect.Should().BeNull();
    }

    [Test]
    public void CreateAdapterOverlayLineSeries_Vpn_HasDashedStroke()
    {
        // Arrange
        var points = new ObservableCollection<DateTimePoint>();
        var color = SKColors.Yellow;

        // Act
        var result = ChartSeriesFactory.CreateAdapterOverlayLineSeries("VPN", points, color, isVpn: true);

        // Assert
        result.Name.Should().Be("VPN");
        result.ScalesYAt.Should().Be(0);

        var stroke = result.Stroke.Should().BeOfType<SolidColorPaint>().Value;
        stroke.StrokeThickness.Should().Be(1.5f);
        stroke.PathEffect.Should().BeOfType<DashEffect>();
    }

    [Test]
    public void CreateTimeXAxes_ReturnsSingleAxisWithCorrectConfiguration()
    {
        // Act
        var result = ChartSeriesFactory.CreateTimeXAxes();

        // Assert
        result.Should().HaveCount(1);
        var axis = result[0].Should().BeOfType<DateTimeAxis>().Value;
        axis.Name.Should().Be("Time");
        axis.TextSize.Should().Be(11);
        axis.NameTextSize.Should().Be(12);
        axis.AnimationsSpeed.Should().Be(TimeSpan.Zero);
        axis.MinStep.Should().Be(TimeSpan.FromSeconds(2).Ticks);
        axis.NamePaint.Should().BeOfType<SolidColorPaint>()
            .Which.Color.Should().Be(ChartColors.AxisNameColor);
        axis.LabelsPaint.Should().BeOfType<SolidColorPaint>()
            .Which.Color.Should().Be(ChartColors.AxisLabelColor);
        axis.SeparatorsPaint.Should().BeOfType<SolidColorPaint>()
            .Which.Color.Should().Be(ChartColors.GridLineColor);
    }

    [Test]
    public void CreateSpeedYAxes_ReturnsSingleAxisWithCorrectConfiguration()
    {
        // Act
        var result = ChartSeriesFactory.CreateSpeedYAxes();

        // Assert
        result.Should().HaveCount(1);
        result[0].Name.Should().Be("Speed");
        result[0].TextSize.Should().Be(11);
        result[0].NameTextSize.Should().Be(12);
        result[0].MinLimit.Should().Be(0);
        result[0].NamePaint.Should().BeOfType<SolidColorPaint>()
            .Which.Color.Should().Be(ChartColors.AxisNameColor);
        result[0].LabelsPaint.Should().BeOfType<SolidColorPaint>()
            .Which.Color.Should().Be(ChartColors.AxisLabelColor);
        result[0].SeparatorsPaint.Should().BeOfType<SolidColorPaint>()
            .Which.Color.Should().Be(ChartColors.GridLineColor);
    }

    [Test]
    public void CreateSpeedYAxes_Labeler_FormatsSpeedValues()
    {
        // Act
        var result = ChartSeriesFactory.CreateSpeedYAxes();

        // Assert
        result[0].Labeler.Should().NotBeNull();
        result[0].Labeler!(0).Should().Be(ByteFormatter.FormatSpeed(0));
        result[0].Labeler!(1024).Should().Be(ByteFormatter.FormatSpeed(1024));
    }

    [Test]
    public void CreateUsageYAxes_ReturnsSingleAxisWithCorrectConfiguration()
    {
        // Act
        var result = ChartSeriesFactory.CreateUsageYAxes();

        // Assert
        result.Should().HaveCount(1);
        result[0].Name.Should().Be("Usage");
        result[0].TextSize.Should().Be(11);
        result[0].NameTextSize.Should().Be(12);
        result[0].MinLimit.Should().Be(0);
        result[0].NamePaint.Should().BeOfType<SolidColorPaint>()
            .Which.Color.Should().Be(ChartColors.AxisNameColor);
        result[0].LabelsPaint.Should().BeOfType<SolidColorPaint>()
            .Which.Color.Should().Be(ChartColors.AxisLabelColor);
        result[0].SeparatorsPaint.Should().BeOfType<SolidColorPaint>()
            .Which.Color.Should().Be(ChartColors.GridLineColor);
    }

    [Test]
    public void CreateUsageYAxes_Labeler_FormatsBytes()
    {
        // Act
        var result = ChartSeriesFactory.CreateUsageYAxes();

        // Assert
        result[0].Labeler.Should().NotBeNull();
        result[0].Labeler!(0).Should().Be(ByteFormatter.FormatBytes(0));
        result[0].Labeler!(1048576).Should().Be(ByteFormatter.FormatBytes(1048576));
    }

    [Test]
    public void CreatePercentageYAxes_ReturnsSingleAxisWithCorrectConfiguration()
    {
        // Act
        var result = ChartSeriesFactory.CreatePercentageYAxes();

        // Assert
        result.Should().HaveCount(1);
        result[0].Name.Should().Be("%");
        result[0].TextSize.Should().Be(11);
        result[0].NameTextSize.Should().Be(12);
        result[0].MinLimit.Should().Be(0);
        result[0].MaxLimit.Should().Be(100);
        result[0].Position.Should().Be(AxisPosition.End);
        result[0].SeparatorsPaint.Should().BeNull();
        result[0].NamePaint.Should().BeOfType<SolidColorPaint>()
            .Which.Color.Should().Be(ChartColors.AxisNameColor);
        result[0].LabelsPaint.Should().BeOfType<SolidColorPaint>()
            .Which.Color.Should().Be(ChartColors.AxisLabelColor);
    }

    [Test]
    public void CreatePercentageYAxes_Labeler_FormatsAsPercentage()
    {
        // Act
        var result = ChartSeriesFactory.CreatePercentageYAxes();

        // Assert
        result[0].Labeler.Should().NotBeNull();
        result[0].Labeler!(50).Should().Be("50%");
        result[0].Labeler!(100).Should().Be("100%");
    }

    [Test]
    public void CreateHourXAxes_ReturnsSingleAxisWithCorrectConfiguration()
    {
        // Act
        var result = ChartSeriesFactory.CreateHourXAxes();

        // Assert
        result.Should().HaveCount(1);
        result[0].Name.Should().Be("Hour");
        result[0].TextSize.Should().Be(10);
        result[0].NameTextSize.Should().Be(11);
        result[0].LabelsRotation.Should().Be(0);
        result[0].NamePaint.Should().BeOfType<SolidColorPaint>()
            .Which.Color.Should().Be(ChartColors.AxisNameColor);
        result[0].LabelsPaint.Should().BeOfType<SolidColorPaint>()
            .Which.Color.Should().Be(ChartColors.AxisLabelColor);
    }

    [Test]
    public void CreateHourlyYAxes_ReturnsSingleAxisWithCorrectConfiguration()
    {
        // Act
        var result = ChartSeriesFactory.CreateHourlyYAxes();

        // Assert
        result.Should().HaveCount(1);
        result[0].Name.Should().Be("Usage");
        result[0].TextSize.Should().Be(10);
        result[0].NameTextSize.Should().Be(11);
        result[0].MinLimit.Should().Be(0);
        result[0].SeparatorsPaint.Should().BeOfType<SolidColorPaint>()
            .Which.Color.Should().Be(ChartColors.GridLineColor);
    }

    [Test]
    public void CreateUsageColumnSeries_ReturnsTwoStackedColumns()
    {
        // Arrange
        IReadOnlyCollection<long> downloadValues = [100L, 200L, 300L];
        IReadOnlyCollection<long> uploadValues = [50L, 100L, 150L];

        // Act
        var result = ChartSeriesFactory.CreateUsageColumnSeries(downloadValues, uploadValues);

        // Assert
        result.Should().HaveCount(2);
        result[0].Name.Should().Be("Download");
        result[1].Name.Should().Be("Upload");
    }

    [Test]
    public void CreateUsageColumnSeries_DownloadColumn_HasCorrectStyling()
    {
        // Arrange
        IReadOnlyCollection<long> downloadValues = [100L, 200L, 300L];
        IReadOnlyCollection<long> uploadValues = [50L, 100L, 150L];

        // Act
        var result = ChartSeriesFactory.CreateUsageColumnSeries(downloadValues, uploadValues);
        var download = result[0].Should().BeOfType<StackedColumnSeries<long>>().Value;

        // Assert
        download.Values.Should().BeSameAs(downloadValues);
        download.Fill.Should().BeOfType<SolidColorPaint>()
            .Which.Color.Should().Be(ChartColors.DownloadColor);
        download.Stroke.Should().BeNull();
        download.MaxBarWidth.Should().Be(40);
        download.Padding.Should().Be(2);
    }

    [Test]
    public void CreateUsageColumnSeries_UploadColumn_HasCorrectStyling()
    {
        // Arrange
        IReadOnlyCollection<long> downloadValues = [100L, 200L, 300L];
        IReadOnlyCollection<long> uploadValues = [50L, 100L, 150L];

        // Act
        var result = ChartSeriesFactory.CreateUsageColumnSeries(downloadValues, uploadValues);
        var upload = result[1].Should().BeOfType<StackedColumnSeries<long>>().Value;

        // Assert
        upload.Values.Should().BeSameAs(uploadValues);
        upload.Fill.Should().BeOfType<SolidColorPaint>()
            .Which.Color.Should().Be(ChartColors.UploadColor);
        upload.Stroke.Should().BeNull();
        upload.MaxBarWidth.Should().Be(40);
        upload.Padding.Should().Be(2);
    }

    [Test]
    public void CreateHourlyColumnSeries_ReturnsTwoStackedColumns()
    {
        // Arrange
        IReadOnlyCollection<long> downloadValues = [100L, 200L, 300L];
        IReadOnlyCollection<long> uploadValues = [50L, 100L, 150L];

        // Act
        var result = ChartSeriesFactory.CreateHourlyColumnSeries(downloadValues, uploadValues);

        // Assert
        result.Should().HaveCount(2);
        result[0].Name.Should().Be("Download");
        result[1].Name.Should().Be("Upload");
    }

    [Test]
    public void CreateHourlyColumnSeries_HasSmallerBarsThanDailyUsage()
    {
        // Arrange
        IReadOnlyCollection<long> downloadValues = [100L];
        IReadOnlyCollection<long> uploadValues = [50L];

        // Act
        var result = ChartSeriesFactory.CreateHourlyColumnSeries(downloadValues, uploadValues);
        var download = result[0].Should().BeOfType<StackedColumnSeries<long>>().Value;
        var upload = result[1].Should().BeOfType<StackedColumnSeries<long>>().Value;

        // Assert
        download.MaxBarWidth.Should().Be(20);
        download.Padding.Should().Be(1);
        download.Stroke.Should().BeNull();
        upload.MaxBarWidth.Should().Be(20);
        upload.Padding.Should().Be(1);
        upload.Stroke.Should().BeNull();
    }

    [Test]
    public void CreateSparklineSeries_Download_UsesDownloadAccentColor()
    {
        // Arrange
        var points = new ObservableCollection<DateTimePoint>();

        // Act
        var result = ChartSeriesFactory.CreateSparklineSeries(points, isDownload: true);

        // Assert
        result.Should().HaveCount(1);
        var series = result[0].Should().BeOfType<LineSeries<DateTimePoint>>().Value;
        series.Values.Should().BeSameAs(points);
        series.Stroke.Should().BeOfType<SolidColorPaint>()
            .Which.Color.Should().Be(ChartColors.DownloadAccentColor);
        ((SolidColorPaint)series.Stroke!).StrokeThickness.Should().Be(2);
        series.Fill.Should().BeOfType<LinearGradientPaint>();
        series.GeometryFill.Should().BeNull();
        series.GeometryStroke.Should().BeNull();
        series.LineSmoothness.Should().Be(0.65);
        series.AnimationsSpeed.Should().Be(TimeSpan.Zero);
    }

    [Test]
    public void CreateSparklineSeries_Upload_UsesUploadAccentColor()
    {
        // Arrange
        var points = new ObservableCollection<DateTimePoint>();

        // Act
        var result = ChartSeriesFactory.CreateSparklineSeries(points, isDownload: false);

        // Assert
        result.Should().HaveCount(1);
        var series = result[0].Should().BeOfType<LineSeries<DateTimePoint>>().Value;
        series.Stroke.Should().BeOfType<SolidColorPaint>()
            .Which.Color.Should().Be(ChartColors.UploadAccentColor);
    }

    [Test]
    public void CreateSparklineAxes_ForXAxis_ReturnsHiddenDateTimeAxis()
    {
        // Act
        var result = ChartSeriesFactory.CreateSparklineAxes(isXAxis: true);

        // Assert
        result.Should().HaveCount(1);
        var axis = result[0].Should().BeOfType<DateTimeAxis>().Value;
        axis.ShowSeparatorLines.Should().BeFalse();
        axis.LabelsPaint.Should().BeNull();
        axis.NamePaint.Should().BeNull();
        axis.TicksPaint.Should().BeNull();
        axis.SubticksPaint.Should().BeNull();
        axis.SeparatorsPaint.Should().BeNull();
        axis.AnimationsSpeed.Should().Be(TimeSpan.Zero);
    }

    [Test]
    public void CreateSparklineAxes_ForYAxis_ReturnsHiddenAxisWithMinLimit()
    {
        // Act
        var result = ChartSeriesFactory.CreateSparklineAxes(isXAxis: false);

        // Assert
        result.Should().HaveCount(1);
        result[0].Should().BeOfType<Axis>();
        result[0].ShowSeparatorLines.Should().BeFalse();
        result[0].LabelsPaint.Should().BeNull();
        result[0].NamePaint.Should().BeNull();
        result[0].TicksPaint.Should().BeNull();
        result[0].SubticksPaint.Should().BeNull();
        result[0].SeparatorsPaint.Should().BeNull();
        result[0].MinLimit.Should().Be(0);
        result[0].AnimationsSpeed.Should().Be(TimeSpan.Zero);
    }
}
