using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Threading;
using Avalonia.VisualTree;
using WireBound.Avalonia.ViewModels;

namespace WireBound.Avalonia.Views;

public partial class MemoryView : UserControl
{
    private MemoryViewModel? _observedViewModel;
    private Vector? _refreshOffset;
    private int _updateVersion;

    public MemoryView()
    {
        InitializeComponent();
        this.FindControl<ListBox>("ConsumerList")?.AddHandler(KeyDownEvent, OnConsumerKeyDown, RoutingStrategies.Tunnel);
        DataContextChanged += (_, _) => ObserveViewModel();
        AttachedToVisualTree += (_, _) => ObserveViewModel();
        DetachedFromVisualTree += (_, _) => StopObserving();
    }

    private void StopObserving()
    {
        if (_observedViewModel is not null)
        {
            _observedViewModel.ConsumersChanging -= OnConsumersChanging;
            _observedViewModel.ConsumersChanged -= OnConsumersChanged;
        }
        _observedViewModel = null;
        _refreshOffset = null;
        _updateVersion++;
    }

    private void ObserveViewModel()
    {
        StopObserving();
        if (TopLevel.GetTopLevel(this) is null || DataContext is not MemoryViewModel vm) return;
        _observedViewModel = vm;
        vm.ConsumersChanging += OnConsumersChanging;
        vm.ConsumersChanged += OnConsumersChanged;
    }

    private ScrollViewer? GetConsumerScrollViewer() => this.FindControl<ListBox>("ConsumerList")?
        .GetVisualDescendants().OfType<ScrollViewer>().FirstOrDefault();

    private void OnConsumersChanging()
    {
        // Capture immediately before replacement, including late container results,
        // so scrolling during a pending query remains the user's current position.
        _refreshOffset ??= GetConsumerScrollViewer()?.Offset;
        if (this.FindControl<ListBox>("ConsumerList") is { } list) list.AutoScrollToSelectedItem = false;
        _updateVersion++;
    }

    private void OnConsumersChanged()
    {
        if (this.FindControl<ListBox>("ConsumerList") is { } list) list.AutoScrollToSelectedItem = true;
        if (_refreshOffset is { } offset)
        {
            var version = _updateVersion;
            // ListBox brings a restored selection into view after its next layout.
            // Restore the viewport afterwards while retaining normal keyboard scrolling.
            Dispatcher.UIThread.Post(() =>
            {
                if (_observedViewModel is null || version != _updateVersion) return;
                if (GetConsumerScrollViewer() is { } scroll) scroll.Offset = offset;
                _refreshOffset = null;
            }, DispatcherPriority.Background);
        }
    }

    private void OnConsumerKeyDown(object? sender, KeyEventArgs e)
    {
        if (DataContext is not MemoryViewModel vm || vm.SelectedConsumer is not { } row) return;
        if (e.Key == Key.Right && row.CanToggle && !row.IsExpanded)
        {
            vm.ToggleConsumerCommand.Execute(row);
            e.Handled = true;
        }
        else if (e.Key == Key.Left)
        {
            if (row.CanToggle && row.IsExpanded) vm.ToggleConsumerCommand.Execute(row);
            else
            {
                var index = vm.Consumers.IndexOf(row);
                vm.SelectedConsumer = vm.Consumers.Take(index).LastOrDefault(r => r.Depth < row.Depth) ?? row;
            }
            e.Handled = true;
        }
    }
}
