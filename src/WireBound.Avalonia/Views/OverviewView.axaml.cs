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
        var shouldBeNarrow = e.NewSize.Width < 920;
        if (shouldBeNarrow == _isNarrow) return;
        _isNarrow = shouldBeNarrow;

        var grid = this.FindControl<Grid>("DashboardContentGrid");
        var activityPanel = this.FindControl<Border>("ActivityPanel");
        var causePanel = this.FindControl<Border>("CausePanel");
        if (grid is null || activityPanel is null || causePanel is null) return;

        grid.ColumnDefinitions.Clear();
        grid.RowDefinitions.Clear();

        if (shouldBeNarrow)
        {
            grid.ColumnDefinitions.Add(new ColumnDefinition(GridLength.Star));
            grid.RowDefinitions.Add(new RowDefinition(GridLength.Star));
            grid.RowDefinitions.Add(new RowDefinition(new GridLength(300)));
            grid.ColumnSpacing = 0;
            grid.RowSpacing = 14;
            Grid.SetColumn(activityPanel, 0);
            Grid.SetRow(activityPanel, 0);
            Grid.SetColumn(causePanel, 0);
            Grid.SetRow(causePanel, 1);
        }
        else
        {
            grid.ColumnDefinitions.Add(new ColumnDefinition(new GridLength(2.2, GridUnitType.Star)));
            grid.ColumnDefinitions.Add(new ColumnDefinition(GridLength.Star));
            grid.RowDefinitions.Add(new RowDefinition(GridLength.Star));
            grid.ColumnSpacing = 14;
            grid.RowSpacing = 0;
            Grid.SetColumn(activityPanel, 0);
            Grid.SetRow(activityPanel, 0);
            Grid.SetColumn(causePanel, 1);
            Grid.SetRow(causePanel, 0);
        }
    }
}
