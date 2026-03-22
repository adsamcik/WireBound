using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;

namespace WireBound.Avalonia.Helpers;

/// <summary>
/// Attached property that blocks mouse wheel events from propagating to parent ScrollViewers.
/// Apply to chart controls so hovering over a chart doesn't scroll the page.
/// </summary>
public static class ScrollBlockBehavior
{
    public static readonly AttachedProperty<bool> BlockScrollProperty =
        AvaloniaProperty.RegisterAttached<Control, bool>(
            "BlockScroll", typeof(ScrollBlockBehavior));

    static ScrollBlockBehavior()
    {
        BlockScrollProperty.Changed.AddClassHandler<Control>(OnBlockScrollChanged);
    }

    public static bool GetBlockScroll(Control element) => element.GetValue(BlockScrollProperty);
    public static void SetBlockScroll(Control element, bool value) => element.SetValue(BlockScrollProperty, value);

    private static void OnBlockScrollChanged(Control control, AvaloniaPropertyChangedEventArgs args)
    {
        if (args.GetNewValue<bool>())
        {
            control.AddHandler(
                InputElement.PointerWheelChangedEvent,
                OnPointerWheelChanged,
                RoutingStrategies.Tunnel);
        }
        else
        {
            control.RemoveHandler(
                InputElement.PointerWheelChangedEvent,
                OnPointerWheelChanged);
        }
    }

    private static void OnPointerWheelChanged(object? sender, PointerWheelEventArgs e)
    {
        e.Handled = true;
    }
}
