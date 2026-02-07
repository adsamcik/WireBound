using Avalonia.Controls;

namespace WireBound.Avalonia.Views;

public partial class OverviewView : UserControl
{
    private bool _isNarrow;

    public OverviewView()
    {
        InitializeComponent();
        SizeChanged += OnSizeChanged;
    }

    private void OnSizeChanged(object? sender, SizeChangedEventArgs e)
    {
        var shouldBeNarrow = e.NewSize.Width < 600;
        if (shouldBeNarrow == _isNarrow) return;
        _isNarrow = shouldBeNarrow;

        var grid = this.FindControl<Grid>("SpeedCardsGrid");
        if (grid is null || grid.Children.Count < 2) return;

        var uploadCard = grid.Children[1] as Control;
        if (uploadCard is null) return;

        if (shouldBeNarrow)
        {
            grid.ColumnDefinitions.Clear();
            grid.ColumnDefinitions.Add(new ColumnDefinition(GridLength.Star));

            if (grid.RowDefinitions.Count < 2)
            {
                grid.RowDefinitions.Clear();
                grid.RowDefinitions.Add(new RowDefinition(GridLength.Auto));
                grid.RowDefinitions.Add(new RowDefinition(GridLength.Auto));
                grid.RowDefinitions.Add(new RowDefinition(GridLength.Auto));
            }

            Grid.SetColumn(uploadCard, 0);
            Grid.SetRow(uploadCard, 1);
        }
        else
        {
            grid.ColumnDefinitions.Clear();
            grid.ColumnDefinitions.Add(new ColumnDefinition(GridLength.Star));
            grid.ColumnDefinitions.Add(new ColumnDefinition(GridLength.Star));

            Grid.SetColumn(uploadCard, 1);
            Grid.SetRow(uploadCard, 0);
        }
    }
}
