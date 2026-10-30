using System.ComponentModel;
using Avalonia;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Media;
using Avalonia.Styling;
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
            _observedViewModel.ConsumerRevealRequested -= OnConsumerRevealRequested;
            _observedViewModel.PropertyChanged -= OnViewModelPropertyChanged;
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
        vm.ConsumerRevealRequested += OnConsumerRevealRequested;
        vm.PropertyChanged += OnViewModelPropertyChanged;
        RenderMemorySegments();
    }

    private void OnViewModelPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(MemoryViewModel.MemorySegments)) RenderMemorySegments();
        else if (e.PropertyName == nameof(MemoryViewModel.SelectedConsumer)) UpdateSegmentSelection();
    }

    private void RenderMemorySegments()
    {
        if (this.FindControl<Grid>("MemoryCapacityBar") is not { } bar || _observedViewModel is not { } vm) return;
        bar.Children.Clear();
        bar.ColumnDefinitions.Clear();
        foreach (var segment in vm.MemorySegments)
        {
            bar.ColumnDefinitions.Add(new ColumnDefinition(new GridLength(segment.Bytes, GridUnitType.Star)));
            var background = new SolidColorBrush(Color.Parse(segment.Color));
            var foreground = (IBrush)this.FindResource(segment.UseLightText ? "PrimaryTextBrush" : "BackgroundBrush")!;
            var label = new TextBlock
            {
                Text = segment.Label,
                FontSize = 11,
                TextTrimming = TextTrimming.CharacterEllipsis,
                VerticalAlignment = global::Avalonia.Layout.VerticalAlignment.Center,
                Foreground = foreground
            };
            Control control;
            if (segment.ConsumerId is not null)
                control = new Button
                {
                    Theme = (ControlTheme)this.FindResource("MemorySegmentButtonTheme")!,
                    Background = background,
                    Foreground = foreground,
                    Content = label,
                    Command = vm.RevealMemoryConsumerCommand,
                    CommandParameter = segment,
                    DataContext = segment
                };
            else
                control = new Border { Background = background, Child = label, ClipToBounds = true, Padding = new Thickness(5, 0) };
            ToolTip.SetTip(control, segment.Tooltip);
            AutomationProperties.SetName(control, segment.Tooltip);
            control.SizeChanged += (_, e) => label.IsVisible = e.NewSize.Width >= 32;
            Grid.SetColumn(control, bar.Children.Count);
            bar.Children.Add(control);
        }
        UpdateSegmentSelection();
    }

    private void UpdateSegmentSelection()
    {
        if (this.FindControl<Grid>("MemoryCapacityBar") is not { } bar) return;
        foreach (var button in bar.Children.OfType<Button>())
            button.Classes.Set("selected", button.DataContext is MemorySegment segment
                && segment.ConsumerId == _observedViewModel?.SelectedConsumer?.Id);
    }

    private void OnConsumerRevealRequested(MemoryConsumerRow row)
    {
        // A bar click deliberately navigates; cancel passive refresh viewport restoration.
        _refreshOffset = null;
        _updateVersion++;
        Dispatcher.UIThread.Post(() =>
        {
            if (_observedViewModel?.SelectedConsumer is not { } current || current.Id != row.Id) return;
            _refreshOffset = null;
            _updateVersion++;
            if (this.FindControl<ListBox>("ConsumerList") is { } list)
            {
                list.ScrollIntoView(current);
                list.Focus();
            }
        }, DispatcherPriority.Background);
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
