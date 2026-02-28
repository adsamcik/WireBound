using CommunityToolkit.Mvvm.ComponentModel;

namespace WireBound.Avalonia.ViewModels;

/// <summary>
/// Navigation item for the sidebar.
/// </summary>
public partial class NavigationItem : ObservableObject
{
    [ObservableProperty]
    private string _title = string.Empty;

    [ObservableProperty]
    private string _icon = string.Empty;

    [ObservableProperty]
    private string _route = string.Empty;

    [ObservableProperty]
    private bool _hasBadge;
}
