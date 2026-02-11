using AwesomeAssertions;
using WireBound.Avalonia.Helpers;
using WireBound.Tests.Fixtures;

namespace WireBound.Tests.Helpers;

/// <summary>
/// Unit tests for ChartDataManager chart data buffering, downsampling, and statistics.
/// </summary>
public class ChartDataManagerTests
{
    public ChartDataManagerTests()
    {
        LiveChartsHook.EnsureInitialized();
    }

    // ÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉ
    // Constructor Tests
    // ÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉ

    [Test]
    public void Constructor_Default_HasExpectedBufferAndDisplaySizes()
    {
        // Act
        var manager = new ChartDataManager();

        // Assert
        manager.MaxBufferSize.Should().Be(3600);
        manager.MaxDisplayPoints.Should().Be(300);
    }

    [Test]
    public void Constructor_CustomSizes_AreRespected()
    {
        // Act
        var manager = new ChartDataManager(maxBufferSize: 500, maxDisplayPoints: 50);

        // Assert
        manager.MaxBufferSize.Should().Be(500);
        manager.MaxDisplayPoints.Should().Be(50);
    }

    // ÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉ
    // AddDataPoint & Statistics Tests
    // ÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉ

    [Test]
    public void AddDataPoint_SinglePoint_SampleCountAndBufferCountAreOne()
    {
        // Arrange
        var manager = new ChartDataManager();

        // Act
        manager.AddDataPoint(DateTime.Now, 1000, 500);

        // Assert
        manager.SampleCount.Should().Be(1);
        manager.BufferCount.Should().Be(1);
    }

    [Test]
    public void AddDataPoint_MultiplePoints_CorrectCounts()
    {
        // Arrange
        var manager = new ChartDataManager();
        var now = DateTime.Now;

        // Act
        manager.AddDataPoint(now, 100, 50);
        manager.AddDataPoint(now.AddSeconds(1), 200, 100);
        manager.AddDataPoint(now.AddSeconds(2), 300, 150);

        // Assert
        manager.SampleCount.Should().Be(3);
        manager.BufferCount.Should().Be(3);
    }

    [Test]
    public void AddDataPoint_PeakDownloadBps_TracksMaximum()
    {
        // Arrange
        var manager = new ChartDataManager();
        var now = DateTime.Now;

        // Act
        manager.AddDataPoint(now, 100, 0);
        manager.AddDataPoint(now.AddSeconds(1), 500, 0);
        manager.AddDataPoint(now.AddSeconds(2), 300, 0);

        // Assert
        manager.PeakDownloadBps.Should().Be(500);
    }

    [Test]
    public void AddDataPoint_PeakUploadBps_TracksMaximum()
    {
        // Arrange
        var manager = new ChartDataManager();
        var now = DateTime.Now;

        // Act
        manager.AddDataPoint(now, 0, 200);
        manager.AddDataPoint(now.AddSeconds(1), 0, 800);
        manager.AddDataPoint(now.AddSeconds(2), 0, 400);

        // Assert
        manager.PeakUploadBps.Should().Be(800);
    }

    [Test]
    public void AddDataPoint_AverageDownloadBps_ComputesCorrectly()
    {
        // Arrange
        var manager = new ChartDataManager();
        var now = DateTime.Now;

        // Act
        manager.AddDataPoint(now, 100, 0);
        manager.AddDataPoint(now.AddSeconds(1), 200, 0);
        manager.AddDataPoint(now.AddSeconds(2), 300, 0);

        // Assert ÔÇö (100 + 200 + 300) / 3 = 200
        manager.AverageDownloadBps.Should().Be(200);
    }

    [Test]
    public void AddDataPoint_AverageUploadBps_ComputesCorrectly()
    {
        // Arrange
        var manager = new ChartDataManager();
        var now = DateTime.Now;

        // Act
        manager.AddDataPoint(now, 0, 400);
        manager.AddDataPoint(now.AddSeconds(1), 0, 600);

        // Assert ÔÇö (400 + 600) / 2 = 500
        manager.AverageUploadBps.Should().Be(500);
    }

    [Test]
    public void AverageDownloadBps_NoSamples_ReturnsZero()
    {
        // Arrange
        var manager = new ChartDataManager();

        // Assert
        manager.AverageDownloadBps.Should().Be(0);
        manager.AverageUploadBps.Should().Be(0);
    }

    // ÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉ
    // Clear Tests
    // ÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉ

    [Test]
    public void Clear_ResetsBufferCountSampleCountAndPeaks()
    {
        // Arrange
        var manager = new ChartDataManager();
        var now = DateTime.Now;
        manager.AddDataPoint(now, 1000, 500);
        manager.AddDataPoint(now.AddSeconds(1), 2000, 1000);

        // Act
        manager.Clear();

        // Assert
        manager.BufferCount.Should().Be(0);
        manager.SampleCount.Should().Be(0);
        manager.PeakDownloadBps.Should().Be(0);
        manager.PeakUploadBps.Should().Be(0);
    }

    // ÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉ
    // ResetStatistics Tests
    // ÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉ

    [Test]
    public void ResetStatistics_ClearsStatsButKeepsBuffer()
    {
        // Arrange
        var manager = new ChartDataManager();
        var now = DateTime.Now;
        manager.AddDataPoint(now, 1000, 500);
        manager.AddDataPoint(now.AddSeconds(1), 2000, 1000);

        // Act
        manager.ResetStatistics();

        // Assert
        manager.SampleCount.Should().Be(0);
        manager.PeakDownloadBps.Should().Be(0);
        manager.PeakUploadBps.Should().Be(0);
        manager.AverageDownloadBps.Should().Be(0);
        manager.AverageUploadBps.Should().Be(0);
        manager.BufferCount.Should().Be(2);
    }

    // ÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉ
    // LoadHistory Tests
    // ÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉ

    [Test]
    public void LoadHistory_AddsAllPointsAndUpdatesStats()
    {
        // Arrange
        var manager = new ChartDataManager();
        var now = DateTime.Now;
        var history = new List<(DateTime, long, long)>
        {
            (now, 100, 50),
            (now.AddSeconds(1), 300, 150),
            (now.AddSeconds(2), 200, 100)
        };

        // Act
        manager.LoadHistory(history);

        // Assert
        manager.BufferCount.Should().Be(3);
        manager.SampleCount.Should().Be(3);
        manager.PeakDownloadBps.Should().Be(300);
        manager.PeakUploadBps.Should().Be(150);
        manager.AverageDownloadBps.Should().Be(200); // (100+300+200)/3
        manager.AverageUploadBps.Should().Be(100);   // (50+150+100)/3
    }

    [Test]
    public void LoadHistory_EmptyEnumerable_NoChange()
    {
        // Arrange
        var manager = new ChartDataManager();

        // Act
        manager.LoadHistory([]);

        // Assert
        manager.BufferCount.Should().Be(0);
        manager.SampleCount.Should().Be(0);
    }

    // ÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉ
    // GetRawData Tests
    // ÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉ

    [Test]
    public void GetRawData_ReturnsAllBufferedData()
    {
        // Arrange
        var manager = new ChartDataManager();
        var now = DateTime.Now;
        manager.AddDataPoint(now, 100, 10);
        manager.AddDataPoint(now.AddSeconds(1), 200, 20);
        manager.AddDataPoint(now.AddSeconds(2), 300, 30);

        // Act
        var raw = manager.GetRawData().ToList();

        // Assert
        raw.Should().HaveCount(3);
        raw[0].Item2.Should().Be(100);
        raw[1].Item2.Should().Be(200);
        raw[2].Item2.Should().Be(300);
    }

    [Test]
    public void GetRawData_WithRangeSeconds_ReturnsOnlyRecentData()
    {
        // Arrange
        var manager = new ChartDataManager();
        var now = DateTime.Now;
        manager.AddDataPoint(now.AddSeconds(-120), 100, 10); // 2 minutes ago
        manager.AddDataPoint(now.AddSeconds(-30), 200, 20);  // 30 seconds ago
        manager.AddDataPoint(now, 300, 30);                   // now

        // Act ÔÇö request last 60 seconds
        var raw = manager.GetRawData(60).ToList();

        // Assert ÔÇö should exclude the 2-minute-old point
        raw.Should().HaveCount(2);
        raw[0].Item2.Should().Be(200);
        raw[1].Item2.Should().Be(300);
    }

    // ÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉ
    // GetDisplayData Tests
    // ÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉ

    [Test]
    public void GetDisplayData_ReturnsDataWithinTimeRange()
    {
        // Arrange
        var manager = new ChartDataManager(maxDisplayPoints: 300);
        var now = DateTime.Now;
        manager.AddDataPoint(now.AddSeconds(-120), 100, 10); // outside 60s range
        manager.AddDataPoint(now.AddSeconds(-30), 200, 20);
        manager.AddDataPoint(now, 300, 30);

        // Act
        var (download, upload) = manager.GetDisplayData(60);

        // Assert
        download.Should().HaveCount(2);
        upload.Should().HaveCount(2);
    }

    [Test]
    public void GetDisplayData_DownsamplesWhenExceedingMaxDisplayPoints()
    {
        // Arrange
        var maxDisplay = 50;
        var manager = new ChartDataManager(maxBufferSize: 1000, maxDisplayPoints: maxDisplay);
        var now = DateTime.Now;

        // Add more points than maxDisplayPoints, all within the range
        for (var i = 0; i < 200; i++)
        {
            manager.AddDataPoint(now.AddSeconds(-200 + i), i * 10, i * 5);
        }

        // Act
        var (download, upload) = manager.GetDisplayData(300);

        // Assert
        download.Should().HaveCount(maxDisplay);
        upload.Should().HaveCount(maxDisplay);
    }

    // ÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉ
    // Buffer Overflow Tests
    // ÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉ

    [Test]
    public void AddDataPoint_ExceedingMaxBufferSize_EvictsOldest()
    {
        // Arrange
        var manager = new ChartDataManager(maxBufferSize: 5, maxDisplayPoints: 300);
        var now = DateTime.Now;

        // Act ÔÇö add 8 points into a buffer of 5
        for (var i = 0; i < 8; i++)
        {
            manager.AddDataPoint(now.AddSeconds(i), (i + 1) * 100, (i + 1) * 10);
        }

        // Assert
        manager.BufferCount.Should().Be(5);

        var raw = manager.GetRawData().ToList();
        raw.Should().HaveCount(5);
        // Oldest 3 (100,200,300) evicted; remaining are 400..800
        raw[0].Item2.Should().Be(400);
        raw[4].Item2.Should().Be(800);
    }
}
