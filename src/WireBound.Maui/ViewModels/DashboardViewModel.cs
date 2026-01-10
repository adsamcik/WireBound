using CommunityToolkit.Mvvm.ComponentModel;
using LiveChartsCore;
using LiveChartsCore.Defaults;
using LiveChartsCore.SkiaSharpView;
using LiveChartsCore.SkiaSharpView.Painting;
using SkiaSharp;
using System.Collections.ObjectModel;
using WireBound.Maui.Helpers;
using WireBound.Maui.Models;
using WireBound.Maui.Services;

namespace WireBound.Maui.ViewModels;

public partial class DashboardViewModel : ObservableObject, IDisposable
{
    private readonly INetworkMonitorService _networkMonitor;
    private bool _disposed;
    private readonly ObservableCollection<DateTimePoint> _downloadSpeedPoints = new();
    private readonly ObservableCollection<DateTimePoint> _uploadSpeedPoints = new();
    private const int MaxDataPoints = 60; // 60 seconds of data

    [ObservableProperty]
    public partial string DownloadSpeed { get; set; }

    [ObservableProperty]
    public partial string UploadSpeed { get; set; }

    [ObservableProperty]
    public partial string SessionDownload { get; set; }

    [ObservableProperty]
    public partial string SessionUpload { get; set; }

    [ObservableProperty]
    public partial string SelectedAdapterName { get; set; }

    [ObservableProperty]
    public partial ObservableCollection<NetworkAdapter> Adapters { get; set; }

    [ObservableProperty]
    public partial NetworkAdapter? SelectedAdapter { get; set; }

    public ISeries[] SpeedSeries { get; }

    public Axis[] XAxes { get; } =
    [
        new DateTimeAxis(TimeSpan.FromSeconds(1), date => date.ToString("HH:mm:ss"))
        {
            Name = "Time",
            NamePaint = new SolidColorPaint(SKColors.Gray),
            LabelsPaint = new SolidColorPaint(SKColors.Gray),
            TextSize = 10,
            AnimationsSpeed = TimeSpan.FromMilliseconds(0),
            MinStep = TimeSpan.FromSeconds(1).Ticks
        }
    ];

    public Axis[] YAxes { get; } =
    [
        new Axis
        {
            Name = "Speed",
            NamePaint = new SolidColorPaint(SKColors.Gray),
            LabelsPaint = new SolidColorPaint(SKColors.Gray),
            TextSize = 10,
            MinLimit = 0,
            Labeler = value => ByteFormatter.FormatSpeed((long)value)
        }
    ];

    public DashboardViewModel(INetworkMonitorService networkMonitor)
    {
        _networkMonitor = networkMonitor;
        _networkMonitor.StatsUpdated += OnStatsUpdated;

        // Initialize observable properties
        DownloadSpeed = "0 B/s";
        UploadSpeed = "0 B/s";
        SessionDownload = "0 B";
        SessionUpload = "0 B";
        SelectedAdapterName = "All Adapters";
        Adapters = new ObservableCollection<NetworkAdapter>();

        // Initialize chart series
        SpeedSeries =
        [
            new LineSeries<DateTimePoint>
            {
                Name = "Download",
                Values = _downloadSpeedPoints,
                Fill = new SolidColorPaint(SKColors.DodgerBlue.WithAlpha(50)),
                Stroke = new SolidColorPaint(SKColors.DodgerBlue, 2),
                GeometryFill = null,
                GeometryStroke = null,
                LineSmoothness = 0.5,
                AnimationsSpeed = TimeSpan.FromMilliseconds(0)
            },
            new LineSeries<DateTimePoint>
            {
                Name = "Upload",
                Values = _uploadSpeedPoints,
                Fill = new SolidColorPaint(SKColors.LimeGreen.WithAlpha(50)),
                Stroke = new SolidColorPaint(SKColors.LimeGreen, 2),
                GeometryFill = null,
                GeometryStroke = null,
                LineSmoothness = 0.5,
                AnimationsSpeed = TimeSpan.FromMilliseconds(0)
            }
        ];

        LoadAdapters();
    }

    private void LoadAdapters()
    {
        Adapters.Clear();
        
        // Add "All Adapters" option
        var allAdapters = new NetworkAdapter { Id = "", Name = "All Adapters" };
        Adapters.Add(allAdapters);
        
        foreach (var adapter in _networkMonitor.GetAdapters())
        {
            Adapters.Add(adapter);
        }

        // Set default selection (will trigger OnSelectedAdapterChanged but that's fine for initial load)
        SelectedAdapter = allAdapters;
    }

    partial void OnSelectedAdapterChanged(NetworkAdapter? value)
    {
        if (value != null)
        {
            _networkMonitor.SetAdapter(value.Id);
            SelectedAdapterName = value.Name;
            _networkMonitor.ResetSession();
            
            // Ensure chart is cleared on UI thread to avoid race conditions
            if (MainThread.IsMainThread)
            {
                ClearChart();
            }
            else
            {
                MainThread.BeginInvokeOnMainThread(ClearChart);
            }
        }
    }

    private void OnStatsUpdated(object? sender, NetworkStats stats)
    {
        // Skip if already disposed to prevent UI updates after cleanup
        if (_disposed) return;

        System.Diagnostics.Debug.WriteLine($"[DashboardVM] StatsUpdated: DL={stats.DownloadSpeedBps} UL={stats.UploadSpeedBps}");

        // Update on UI thread using MAUI dispatcher
        MainThread.BeginInvokeOnMainThread(() =>
        {
            // Double-check disposed state on UI thread
            if (_disposed) return;

            DownloadSpeed = stats.DownloadSpeedFormatted;
            UploadSpeed = stats.UploadSpeedFormatted;
            SessionDownload = stats.SessionReceivedFormatted;
            SessionUpload = stats.SessionSentFormatted;

            // Update chart
            var now = stats.Timestamp;
            _downloadSpeedPoints.Add(new DateTimePoint(now, stats.DownloadSpeedBps));
            _uploadSpeedPoints.Add(new DateTimePoint(now, stats.UploadSpeedBps));

            // Keep only last N points
            while (_downloadSpeedPoints.Count > MaxDataPoints)
            {
                _downloadSpeedPoints.RemoveAt(0);
                _uploadSpeedPoints.RemoveAt(0);
            }
        });
    }

    private void ClearChart()
    {
        _downloadSpeedPoints.Clear();
        _uploadSpeedPoints.Clear();
    }

    public void Dispose()
    {
        Dispose(true);
        GC.SuppressFinalize(this);
    }

    protected virtual void Dispose(bool disposing)
    {
        if (_disposed) return;

        if (disposing)
        {
            _networkMonitor.StatsUpdated -= OnStatsUpdated;
        }

        _disposed = true;
    }
}
