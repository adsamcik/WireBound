using CommunityToolkit.Mvvm.ComponentModel;

namespace WireBound.Avalonia.ViewModels;

/// <summary>
/// Navigation item for the sidebar.
/// </summary>
public partial class NavigationItem : ObservableObject
{
    [ObservableProperty]
    private string _title = string.Empty;

    /// <summary>
    /// Resource key into the Wire Trace icon set, e.g. <c>"WbNavOverview"</c>.
    /// Resolved at render time by <see cref="WireBound.Avalonia.Converters.IconKeyToBitmapConverter"/>
    /// into a bundled PNG, with hand-drawn vector geometry as a fallback for
    /// keys without a raster. The Wire Trace system replaced the legacy
    /// emoji <c>Icon</c> string.
    /// </summary>
    [ObservableProperty]
    private string _iconKey = string.Empty;

    [ObservableProperty]
    private string _route = string.Empty;

    [ObservableProperty]
    private bool _hasBadge;
}
