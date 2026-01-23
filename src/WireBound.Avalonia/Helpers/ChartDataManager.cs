using LiveChartsCore.Defaults;
using WireBound.Core.Helpers;

namespace WireBound.Avalonia.Helpers;

/// <summary>
/// Manages chart data buffering, downsampling, and statistics for network speed charts.
/// Encapsulates the data storage and provides filtered/downsampled data for display.
/// </summary>
public class ChartDataManager
{
    private readonly CircularBuffer<(DateTime Time, long Download, long Upload)> _dataBuffer;
    private readonly int _maxDisplayPoints;

    // Statistics tracking
    private long _peakDownloadBps;
    private long _peakUploadBps;
    private long _totalDownloadBps;
    private long _totalUploadBps;
    private int _sampleCount;

    /// <summary>
    /// Maximum number of data points to store in the buffer.
    /// </summary>
    public int MaxBufferSize { get; }

    /// <summary>
    /// Maximum number of points to display after downsampling.
    /// </summary>
    public int MaxDisplayPoints => _maxDisplayPoints;

    /// <summary>
    /// Peak download speed in bytes per second since last reset.
    /// </summary>
    public long PeakDownloadBps => _peakDownloadBps;

    /// <summary>
    /// Peak upload speed in bytes per second since last reset.
    /// </summary>
    public long PeakUploadBps => _peakUploadBps;

    /// <summary>
    /// Average download speed in bytes per second since last reset.
    /// </summary>
    public long AverageDownloadBps => _sampleCount > 0 ? _totalDownloadBps / _sampleCount : 0;

    /// <summary>
    /// Average upload speed in bytes per second since last reset.
    /// </summary>
    public long AverageUploadBps => _sampleCount > 0 ? _totalUploadBps / _sampleCount : 0;

    /// <summary>
    /// Number of samples collected since last reset.
    /// </summary>
    public int SampleCount => _sampleCount;

    /// <summary>
    /// Number of data points currently in the buffer.
    /// </summary>
    public int BufferCount => _dataBuffer.Count;

    /// <summary>
    /// Creates a new ChartDataManager with the specified buffer and display limits.
    /// </summary>
    /// <param name="maxBufferSize">Maximum number of data points to store (default: 3600 for 1 hour at 1Hz)</param>
    /// <param name="maxDisplayPoints">Maximum points to display after downsampling (default: 300)</param>
    public ChartDataManager(int maxBufferSize = 3600, int maxDisplayPoints = 300)
    {
        MaxBufferSize = maxBufferSize;
        _maxDisplayPoints = maxDisplayPoints;
        _dataBuffer = new CircularBuffer<(DateTime Time, long Download, long Upload)>(maxBufferSize);
    }

    /// <summary>
    /// Adds a new data point to the buffer and updates statistics.
    /// </summary>
    /// <param name="time">Timestamp of the data point</param>
    /// <param name="downloadBps">Download speed in bytes per second</param>
    /// <param name="uploadBps">Upload speed in bytes per second</param>
    public void AddDataPoint(DateTime time, long downloadBps, long uploadBps)
    {
        _dataBuffer.Add((time, downloadBps, uploadBps));
        UpdateStatistics(downloadBps, uploadBps);
    }

    /// <summary>
    /// Updates peak and average statistics with a new sample.
    /// </summary>
    private void UpdateStatistics(long downloadBps, long uploadBps)
    {
        if (downloadBps > _peakDownloadBps)
            _peakDownloadBps = downloadBps;

        if (uploadBps > _peakUploadBps)
            _peakUploadBps = uploadBps;

        _totalDownloadBps += downloadBps;
        _totalUploadBps += uploadBps;
        _sampleCount++;
    }

    /// <summary>
    /// Gets buffered data filtered by time range, applying downsampling if needed.
    /// </summary>
    /// <param name="rangeSeconds">Number of seconds to include from now</param>
    /// <returns>
    /// Tuple containing download points and upload points, downsampled if exceeding MaxDisplayPoints.
    /// </returns>
    public (List<DateTimePoint> Download, List<DateTimePoint> Upload) GetDisplayData(int rangeSeconds)
    {
        var cutoff = DateTime.Now.AddSeconds(-rangeSeconds);
        var relevantData = _dataBuffer.AsEnumerable()
            .Where(d => d.Time >= cutoff)
            .ToList();

        var downloadPoints = relevantData
            .Select(d => new DateTimePoint(d.Time, d.Download))
            .ToList();
        var uploadPoints = relevantData
            .Select(d => new DateTimePoint(d.Time, d.Upload))
            .ToList();

        // Apply LTTB downsampling if we have too many points
        if (downloadPoints.Count > _maxDisplayPoints)
        {
            downloadPoints = LttbDownsampler.Downsample(downloadPoints, _maxDisplayPoints);
            uploadPoints = LttbDownsampler.Downsample(uploadPoints, _maxDisplayPoints);
        }

        return (downloadPoints, uploadPoints);
    }

    /// <summary>
    /// Gets all buffered data as enumerable (no filtering or downsampling).
    /// </summary>
    public IEnumerable<(DateTime Time, long Download, long Upload)> GetRawData()
    {
        return _dataBuffer.AsEnumerable();
    }

    /// <summary>
    /// Gets buffered data filtered by time range without downsampling.
    /// </summary>
    /// <param name="rangeSeconds">Number of seconds to include from now</param>
    public IEnumerable<(DateTime Time, long Download, long Upload)> GetRawData(int rangeSeconds)
    {
        var cutoff = DateTime.Now.AddSeconds(-rangeSeconds);
        return _dataBuffer.AsEnumerable().Where(d => d.Time >= cutoff);
    }

    /// <summary>
    /// Clears all buffered data and resets statistics.
    /// </summary>
    public void Clear()
    {
        _dataBuffer.Clear();
        ResetStatistics();
    }

    /// <summary>
    /// Resets statistics without clearing the buffer.
    /// </summary>
    public void ResetStatistics()
    {
        _peakDownloadBps = 0;
        _peakUploadBps = 0;
        _totalDownloadBps = 0;
        _totalUploadBps = 0;
        _sampleCount = 0;
    }

    /// <summary>
    /// Loads historical data into the buffer and updates statistics.
    /// </summary>
    /// <param name="history">Historical data points to load</param>
    public void LoadHistory(IEnumerable<(DateTime Time, long Download, long Upload)> history)
    {
        foreach (var (time, download, upload) in history)
        {
            _dataBuffer.Add((time, download, upload));
            UpdateStatistics(download, upload);
        }
    }
}
