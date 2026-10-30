using Avalonia;
using CommunityToolkit.Mvvm.ComponentModel;
using WireBound.Core.Helpers;
using WireBound.Avalonia.Services;

namespace WireBound.Avalonia.ViewModels;

/// <summary>One row in the virtualized, expanded portion of the memory hierarchy.</summary>
public sealed partial class MemoryConsumerRow : ObservableObject
{
    public required string Id { get; init; }
    public required string Name { get; init; }
    public string Summary { get; init; } = "";
    public string Detail { get; init; } = "";
    public long? PhysicalBytes { get; init; }
    public bool IsPartial { get; init; }
    public long? ObservedBytes { get; init; }
    public string Metric { get; init; } = "";
    public double SharePercent { get; init; }
    public double BarWidth => Math.Clamp(SharePercent, 0, 100) * 1.3;
    public int Depth { get; set; }
    public string? ContainerId { get; init; }
    public bool HasConfirmedProcessLinks { get; init; }
    public IReadOnlySet<ProcessInstanceId> Processes { get; init; } = new HashSet<ProcessInstanceId>();
    public List<MemoryConsumerRow> Children { get; } = [];
    public bool ChildrenLoaded { get; set; }
    [ObservableProperty] private bool _isExpanded;
    [ObservableProperty] private bool _isLoading;
    [ObservableProperty] private bool _isFilterExpanded;
    public bool CanExpand => Children.Count > 0 || ContainerId is not null;
    public bool CanToggle => CanExpand && !IsFilterExpanded;
    public bool CanLink => Processes.Count > 0;
    public Thickness Indent => new(Depth * 18, 0, 0, 0);
    public string PhysicalDisplay => PhysicalBytes is { } bytes ? (IsPartial ? "≥ " : "") + ByteFormatter.FormatBytes(bytes) : "—";
    public string ObservedDisplay => ObservedBytes is { } bytes ? ByteFormatter.FormatBytes(bytes) : "—";
    public string ExpandGlyph => IsLoading ? "…" : IsExpanded || IsFilterExpanded ? "⌄" : "›";
    public string ExpandName => IsFilterExpanded ? $"Matching children of {Name}" : $"{(IsExpanded ? "Collapse" : "Expand")} {Name}";
    partial void OnIsFilterExpandedChanged(bool value)
    {
        OnPropertyChanged(nameof(CanToggle));
        OnPropertyChanged(nameof(ExpandGlyph));
        OnPropertyChanged(nameof(ExpandName));
    }
    partial void OnIsExpandedChanged(bool value)
    {
        OnPropertyChanged(nameof(ExpandGlyph));
        OnPropertyChanged(nameof(ExpandName));
    }
    partial void OnIsLoadingChanged(bool value) => OnPropertyChanged(nameof(ExpandGlyph));
}
