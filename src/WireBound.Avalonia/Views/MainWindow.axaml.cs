using Avalonia.Controls;

namespace WireBound.Avalonia.Views;

public partial class MainWindow : Window
{
    private bool _usesCompactViewControls;

    public MainWindow()
    {
        InitializeComponent();
        SizeChanged += OnSizeChanged;
    }

    private void OnSizeChanged(object? sender, SizeChangedEventArgs e)
    {
        var useCompactControls = e.NewSize.Width < 1120;
        if (useCompactControls == _usesCompactViewControls) return;
        _usesCompactViewControls = useCompactControls;

        if (this.FindControl<StackPanel>("ExpandedFilterControls") is { } expanded)
        {
            expanded.IsVisible = !useCompactControls;
        }

        if (this.FindControl<Button>("CompactViewButton") is { } compact)
        {
            compact.IsVisible = useCompactControls;
        }
    }
}
