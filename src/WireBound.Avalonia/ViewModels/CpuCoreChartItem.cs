using CommunityToolkit.Mvvm.ComponentModel;
using System.Collections.ObjectModel;

namespace WireBound.Avalonia.ViewModels;

/// <summary>
/// Lightweight per-core CPU chart state used by the Overview dashboard.
/// </summary>
public sealed partial class CpuCoreChartItem : ObservableObject
{
    internal const int MaxHistoryPoints = 60;

    public CpuCoreChartItem(int index)
    {
        Index = index;
    }

    /// <summary>
    /// Zero-based logical processor index.
    /// </summary>
    public int Index { get; }

    public string Label => $"CPU {Index}";

    public string AccessibilityName => $"CPU {Index} usage history";

    public ObservableCollection<double> UsageHistory { get; } = [];

    public ObservableCollection<double> KernelHistory { get; } = [];

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(UsageFormatted))]
    private double _usagePercent;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(KernelFormatted))]
    private double _kernelPercent;

    [ObservableProperty]
    private bool _showKernelHighlight;

    public string UsageFormatted => $"{UsagePercent:F0}%";

    public string KernelFormatted => $"{KernelPercent:F0}% kernel";

    /// <summary>
    /// Adds a sample while retaining a compact one-minute history.
    /// </summary>
    public void AddSample(double usagePercent, double kernelPercent)
    {
        UsagePercent = ClampPercent(usagePercent);
        KernelPercent = Math.Min(UsagePercent, ClampPercent(kernelPercent));

        UsageHistory.Add(UsagePercent);
        KernelHistory.Add(KernelPercent);

        TrimHistory(UsageHistory);
        TrimHistory(KernelHistory);
    }

    private static double ClampPercent(double value) =>
        double.IsFinite(value) ? Math.Clamp(value, 0, 100) : 0;

    private static void TrimHistory(ObservableCollection<double> history)
    {
        while (history.Count > MaxHistoryPoints)
        {
            history.RemoveAt(0);
        }
    }
}
