using Avalonia.Controls;

namespace WireBound.Avalonia.Views;

public partial class MainWindow : Window
{
    private bool _isNavCollapsed;

    public MainWindow()
    {
        InitializeComponent();
        SizeChanged += OnSizeChanged;
    }

    private void OnSizeChanged(object? sender, SizeChangedEventArgs e)
    {
        var shouldCollapse = e.NewSize.Width < 1200;
        if (shouldCollapse == _isNavCollapsed) return;
        _isNavCollapsed = shouldCollapse;

        var rootGrid = this.FindControl<Grid>("RootGrid");
        if (rootGrid is null || rootGrid.ColumnDefinitions.Count == 0) return;

        rootGrid.ColumnDefinitions[0].Width = shouldCollapse
            ? new GridLength(64)
            : new GridLength(240);

        SetNavElementVisibility(!shouldCollapse);
    }

    private void SetNavElementVisibility(bool visible)
    {
        if (this.FindControl<StackPanel>("NavTitleText") is { } title)
            title.IsVisible = visible;
        if (this.FindControl<Grid>("NavWireDivider") is { } divider)
            divider.IsVisible = visible;
        if (this.FindControl<TextBlock>("StatusText") is { } status)
            status.IsVisible = visible;
        if (this.FindControl<TextBlock>("VersionText") is { } version)
            version.IsVisible = visible;
    }
}
