using System.ComponentModel;
using System.Globalization;
using Avalonia;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Media;
using Avalonia.Styling;
using Avalonia.Threading;
using Avalonia.VisualTree;
using WireBound.Core.Helpers;
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
            // These fixed foregrounds are the same pair used to calculate segment contrast.
            var foreground = new SolidColorBrush(Color.Parse(segment.UseLightText ? "#F4F7FA" : "#0B1017"));
            var label = new TextBlock
            {
                Text = segment.Label,
                FontSize = 12,
                FontWeight = FontWeight.SemiBold,
                TextTrimming = TextTrimming.CharacterEllipsis,
                TextAlignment = TextAlignment.Center,
                Foreground = foreground
            };
            var size = new TextBlock
            {
                Text = ByteFormatter.FormatBytes(segment.Bytes),
                FontSize = 11,
                TextAlignment = TextAlignment.Center,
                Foreground = foreground
            };
            var content = new StackPanel
            {
                VerticalAlignment = global::Avalonia.Layout.VerticalAlignment.Center,
                Children = { label, size }
            };
            Control control;
            if (segment.ConsumerId is not null)
                control = new Button
                {
                    Theme = (ControlTheme)this.FindResource("MemorySegmentButtonTheme")!,
                    Background = background,
                    Foreground = foreground,
                    Content = content,
                    Command = vm.RevealMemoryConsumerCommand,
                    CommandParameter = segment,
                    DataContext = segment
                };
            else
                control = new Border
                {
                    Background = background,
                    Child = content,
                    ClipToBounds = true,
                    Padding = new Thickness(2, 0),
                    BorderThickness = new Thickness(2),
                    BorderBrush = Brushes.Transparent,
                    Focusable = true,
                    DataContext = segment
                };
            ToolTip.SetTip(control, new TextBlock { Text = segment.Tooltip, MaxWidth = 340, TextWrapping = TextWrapping.Wrap });
            ToolTip.SetShowDelay(control, 180);
            control.GotFocus += (_, _) =>
            {
                if (control is Border border) border.BorderBrush = foreground;
                ToolTip.SetIsOpen(control, true);
            };
            control.LostFocus += (_, _) =>
            {
                if (control is Border border) border.BorderBrush = Brushes.Transparent;
                ToolTip.SetIsOpen(control, false);
            };
            AutomationProperties.SetName(control, segment.Tooltip);
            control.SizeChanged += (_, e) =>
            {
                double Width(TextBlock text) => new FormattedText(text.Text ?? "", CultureInfo.CurrentCulture,
                    FlowDirection.LeftToRight, new Typeface(text.FontFamily, text.FontStyle, text.FontWeight),
                    text.FontSize, foreground).Width;
                var availableWidth = Math.Max(0, e.NewSize.Width - 8);
                label.FontSize = 12;
                label.Text = segment.Label;
                if (Width(label) > availableWidth && segment.ConsumerId?.StartsWith("host:", StringComparison.Ordinal) == true)
                    label.Text = segment.Label.Replace(" utility VM host", "", StringComparison.Ordinal)
                        .Replace(" VM host", "", StringComparison.Ordinal);
                if (Width(label) > availableWidth && availableWidth < 80) label.FontSize = 11;
                // Never turn a short name into a fragment, or clip a memory value.
                label.IsVisible = Width(label) <= availableWidth || availableWidth >= 80;
                size.IsVisible = Width(size) <= availableWidth;
            };
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
