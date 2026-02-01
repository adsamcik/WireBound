using LiveChartsCore;
using LiveChartsCore.Defaults;
using LiveChartsCore.SkiaSharpView;
using LiveChartsCore.SkiaSharpView.Painting;
using LiveChartsCore.SkiaSharpView.Painting.Effects;
using SkiaSharp;
using System.Collections.ObjectModel;
using WireBound.Core.Helpers;

namespace WireBound.Avalonia.Helpers;

/// <summary>
/// Factory for creating consistently-styled chart series and axes.
/// Centralizes chart styling to ensure visual consistency across the application.
/// </summary>
public static class ChartSeriesFactory
{
    /// <summary>
    /// Creates a pair of LineSeries for real-time speed monitoring (download and upload).
    /// </summary>
    /// <param name="downloadPoints">Observable collection for download data points.</param>
    /// <param name="uploadPoints">Observable collection for upload data points.</param>
    /// <returns>Array containing download and upload LineSeries.</returns>
    public static ISeries[] CreateSpeedLineSeries(
        ObservableCollection<DateTimePoint> downloadPoints,
        ObservableCollection<DateTimePoint> uploadPoints)
    {
        var downloadColor = ChartColors.DownloadAccentColor;
        var uploadColor = ChartColors.UploadAccentColor;

        return
        [
            new LineSeries<DateTimePoint>
            {
                Name = "Download",
                Values = downloadPoints,
                Fill = new LinearGradientPaint(
                    [downloadColor.WithAlpha(100), downloadColor.WithAlpha(0)],
                    new SKPoint(0.5f, 0),
                    new SKPoint(0.5f, 1)
                ),
                Stroke = new SolidColorPaint(downloadColor, 2),
                GeometryFill = null,
                GeometryStroke = null,
                LineSmoothness = 1,
                AnimationsSpeed = TimeSpan.Zero
            },
            new LineSeries<DateTimePoint>
            {
                Name = "Upload",
                Values = uploadPoints,
                Fill = new LinearGradientPaint(
                    [uploadColor.WithAlpha(100), uploadColor.WithAlpha(0)],
                    new SKPoint(0.5f, 0),
                    new SKPoint(0.5f, 1)
                ),
                Stroke = new SolidColorPaint(uploadColor, 2),
                GeometryFill = null,
                GeometryStroke = null,
                LineSmoothness = 1,
                AnimationsSpeed = TimeSpan.Zero
            }
        ];
    }

    /// <summary>
    /// Creates a DateTimeAxis configured for real-time speed monitoring.
    /// </summary>
    /// <returns>Array containing a single DateTimeAxis.</returns>
    public static Axis[] CreateTimeXAxes()
    {
        return
        [
            new DateTimeAxis(TimeSpan.FromSeconds(1), date => date.ToString("HH:mm:ss"))
            {
                Name = "Time",
                NamePaint = new SolidColorPaint(ChartColors.AxisNameColor),
                LabelsPaint = new SolidColorPaint(ChartColors.AxisLabelColor),
                TextSize = 11,
                NameTextSize = 12,
                AnimationsSpeed = TimeSpan.Zero,
                MinStep = TimeSpan.FromSeconds(2).Ticks,
                SeparatorsPaint = new SolidColorPaint(ChartColors.GridLineColor)
            }
        ];
    }

    /// <summary>
    /// Creates a Y-axis configured for speed display (B/s, KB/s, MB/s, etc.).
    /// </summary>
    /// <returns>Array containing a single Axis for speed values.</returns>
    public static Axis[] CreateSpeedYAxes()
    {
        return
        [
            new Axis
            {
                Name = "Speed",
                NamePaint = new SolidColorPaint(ChartColors.AxisNameColor),
                LabelsPaint = new SolidColorPaint(ChartColors.AxisLabelColor),
                TextSize = 11,
                NameTextSize = 12,
                MinLimit = 0,
                SeparatorsPaint = new SolidColorPaint(ChartColors.GridLineColor),
                Labeler = value => ByteFormatter.FormatSpeed((long)value)
            }
        ];
    }

    /// <summary>
    /// Creates a secondary Y-axis for percentage values (0-100%), positioned on the right side.
    /// Used for CPU/Memory overlay series.
    /// </summary>
    /// <returns>Array containing a single Axis for percentage values.</returns>
    public static Axis[] CreatePercentageYAxes()
    {
        return
        [
            new Axis
            {
                Name = "%",
                NamePaint = new SolidColorPaint(ChartColors.AxisNameColor),
                LabelsPaint = new SolidColorPaint(ChartColors.AxisLabelColor),
                TextSize = 11,
                NameTextSize = 12,
                MinLimit = 0,
                MaxLimit = 100,
                Position = LiveChartsCore.Measure.AxisPosition.End,
                SeparatorsPaint = null, // No gridlines for secondary axis
                Labeler = value => $"{value:F0}%"
            }
        ];
    }

    /// <summary>
    /// Creates an overlay line series for system metrics (CPU, Memory).
    /// Uses dashed lines to distinguish from primary network series.
    /// </summary>
    /// <param name="name">Series name for legend/tooltip.</param>
    /// <param name="points">Observable collection for data points.</param>
    /// <param name="color">The color for the series.</param>
    /// <param name="useDashedLine">Whether to use dashed line style.</param>
    /// <returns>A LineSeries configured for overlay display.</returns>
    public static LineSeries<DateTimePoint> CreateOverlayLineSeries(
        string name,
        ObservableCollection<DateTimePoint> points,
        SKColor color,
        bool useDashedLine = true)
    {
        var stroke = new SolidColorPaint(color, 2);
        if (useDashedLine)
        {
            stroke.PathEffect = new DashEffect([6, 4]);
        }

        return new LineSeries<DateTimePoint>
        {
            Name = name,
            Values = points,
            Fill = null, // No fill for overlay lines
            Stroke = stroke,
            GeometryFill = null,
            GeometryStroke = null,
            LineSmoothness = 0.5,
            AnimationsSpeed = TimeSpan.Zero,
            ScalesYAt = 1 // Use secondary Y-axis (index 1)
        };
    }

    /// <summary>
    /// Creates a Y-axis configured for data usage display (B, KB, MB, GB, etc.).
    /// </summary>
    /// <returns>Array containing a single Axis for usage values.</returns>
    public static Axis[] CreateUsageYAxes()
    {
        return
        [
            new Axis
            {
                Name = "Usage",
                NamePaint = new SolidColorPaint(ChartColors.AxisNameColor),
                LabelsPaint = new SolidColorPaint(ChartColors.AxisLabelColor),
                TextSize = 11,
                NameTextSize = 12,
                MinLimit = 0,
                SeparatorsPaint = new SolidColorPaint(ChartColors.GridLineColor),
                Labeler = value => ByteFormatter.FormatBytes((long)value)
            }
        ];
    }

    /// <summary>
    /// Creates a stacked ColumnSeries for daily usage (download + upload).
    /// </summary>
    /// <param name="downloadValues">Download values for each day.</param>
    /// <param name="uploadValues">Upload values for each day.</param>
    /// <returns>Array containing download and upload ColumnSeries.</returns>
    public static ISeries[] CreateUsageColumnSeries(
        IReadOnlyCollection<long> downloadValues,
        IReadOnlyCollection<long> uploadValues)
    {
        return
        [
            new StackedColumnSeries<long>
            {
                Name = "Download",
                Values = downloadValues,
                Fill = new SolidColorPaint(ChartColors.DownloadColor),
                Stroke = null,
                MaxBarWidth = 40,
                Padding = 2
            },
            new StackedColumnSeries<long>
            {
                Name = "Upload",
                Values = uploadValues,
                Fill = new SolidColorPaint(ChartColors.UploadColor),
                Stroke = null,
                MaxBarWidth = 40,
                Padding = 2
            }
        ];
    }

    /// <summary>
    /// Creates a stacked ColumnSeries for hourly usage breakdown.
    /// </summary>
    /// <param name="downloadValues">Download values for each hour.</param>
    /// <param name="uploadValues">Upload values for each hour.</param>
    /// <returns>Array containing download and upload ColumnSeries.</returns>
    public static ISeries[] CreateHourlyColumnSeries(
        IReadOnlyCollection<long> downloadValues,
        IReadOnlyCollection<long> uploadValues)
    {
        return
        [
            new StackedColumnSeries<long>
            {
                Name = "Download",
                Values = downloadValues,
                Fill = new SolidColorPaint(ChartColors.DownloadColor.WithAlpha(200)),
                Stroke = null,
                MaxBarWidth = 20,
                Padding = 1
            },
            new StackedColumnSeries<long>
            {
                Name = "Upload",
                Values = uploadValues,
                Fill = new SolidColorPaint(ChartColors.UploadColor.WithAlpha(200)),
                Stroke = null,
                MaxBarWidth = 20,
                Padding = 1
            }
        ];
    }

    /// <summary>
    /// Creates an X-axis configured for hourly display.
    /// </summary>
    /// <returns>Array containing a single Axis for hour labels.</returns>
    public static Axis[] CreateHourXAxes()
    {
        return
        [
            new Axis
            {
                Name = "Hour",
                NamePaint = new SolidColorPaint(ChartColors.AxisNameColor),
                LabelsPaint = new SolidColorPaint(ChartColors.AxisLabelColor),
                TextSize = 10,
                NameTextSize = 11,
                LabelsRotation = 0
            }
        ];
    }

    /// <summary>
    /// Creates Y-axes configured for hourly usage display.
    /// </summary>
    /// <returns>Array containing a single Axis for hourly usage values.</returns>
    public static Axis[] CreateHourlyYAxes()
    {
        return
        [
            new Axis
            {
                Name = "Usage",
                NamePaint = new SolidColorPaint(ChartColors.AxisNameColor),
                LabelsPaint = new SolidColorPaint(ChartColors.AxisLabelColor),
                TextSize = 10,
                NameTextSize = 11,
                MinLimit = 0,
                SeparatorsPaint = new SolidColorPaint(ChartColors.GridLineColor),
                Labeler = value => ByteFormatter.FormatBytes((long)value)
            }
        ];
    }

    /// <summary>
    /// Creates a minimal sparkline series for inline card charts.
    /// </summary>
    /// <param name="points">Observable collection for data points.</param>
    /// <param name="isDownload">Whether this is a download (cyan) or upload (orange) sparkline.</param>
    /// <returns>Array containing a single LineSeries for the sparkline.</returns>
    public static ISeries[] CreateSparklineSeries(
        ObservableCollection<DateTimePoint> points,
        bool isDownload)
    {
        var color = isDownload ? ChartColors.DownloadAccentColor : ChartColors.UploadAccentColor;

        return
        [
            new LineSeries<DateTimePoint>
            {
                Values = points,
                Fill = new LinearGradientPaint(
                    [color.WithAlpha(80), color.WithAlpha(0)],
                    new SKPoint(0.5f, 0),
                    new SKPoint(0.5f, 1)
                ),
                Stroke = new SolidColorPaint(color, 2),
                GeometryFill = null,
                GeometryStroke = null,
                LineSmoothness = 0.65,
                AnimationsSpeed = TimeSpan.Zero
            }
        ];
    }

    /// <summary>
    /// Creates hidden axes for sparkline charts (no labels, no gridlines).
    /// </summary>
    /// <param name="isXAxis">Whether to create X-axis (time) or Y-axis (value).</param>
    /// <returns>Array containing a single hidden Axis.</returns>
    public static Axis[] CreateSparklineAxes(bool isXAxis)
    {
        if (isXAxis)
        {
            return
            [
                new DateTimeAxis(TimeSpan.FromSeconds(1), _ => string.Empty)
                {
                    ShowSeparatorLines = false,
                    LabelsPaint = null,
                    NamePaint = null,
                    TicksPaint = null,
                    SubticksPaint = null,
                    SeparatorsPaint = null,
                    AnimationsSpeed = TimeSpan.Zero
                }
            ];
        }

        return
        [
            new Axis
            {
                ShowSeparatorLines = false,
                LabelsPaint = null,
                NamePaint = null,
                TicksPaint = null,
                SubticksPaint = null,
                SeparatorsPaint = null,
                MinLimit = 0,
                AnimationsSpeed = TimeSpan.Zero
            }
        ];
    }
}
