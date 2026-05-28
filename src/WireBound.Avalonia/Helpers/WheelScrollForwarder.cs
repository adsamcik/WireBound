using System;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.VisualTree;

namespace WireBound.Avalonia.Helpers;

/// <summary>
/// Attached behavior that forwards mouse-wheel events from a control to its
/// ancestor <see cref="ScrollViewer"/>.
/// </summary>
/// <remarks>
/// <para>
/// LiveCharts chart controls subscribe to <see cref="InputElement.PointerWheelChangedEvent"/>
/// to zoom on scroll. Once inside the chart's hit area the wheel event is consumed
/// before it can bubble up to a parent <see cref="ScrollViewer"/>, so the surrounding
/// page becomes unscrollable whenever the cursor sits over a chart.
/// </para>
/// <para>
/// Attaching this behavior intercepts the wheel event in the tunnel phase (before
/// the chart's own handlers run), translates it into <see cref="ScrollViewer.LineUp"/>/
/// <see cref="ScrollViewer.LineDown"/> calls on the nearest ancestor, and marks the
/// event handled so the chart no longer zooms in response.
/// </para>
/// </remarks>
public static class WheelScrollForwarder
{
    /// <summary>
    /// Number of <see cref="ScrollViewer.LineUp"/>/<see cref="ScrollViewer.LineDown"/>
    /// steps emitted per unit of wheel delta. Matches the OS-standard
    /// "three lines per wheel notch" convention.
    /// </summary>
    private const int LinesPerWheelNotch = 3;

    public static readonly AttachedProperty<bool> ForwardToScrollViewerProperty =
        AvaloniaProperty.RegisterAttached<Control, bool>(
            "ForwardToScrollViewer", typeof(WheelScrollForwarder));

    static WheelScrollForwarder()
    {
        ForwardToScrollViewerProperty.Changed.AddClassHandler<Control>(OnForwardToScrollViewerChanged);
    }

    public static bool GetForwardToScrollViewer(Control element) =>
        element.GetValue(ForwardToScrollViewerProperty);

    public static void SetForwardToScrollViewer(Control element, bool value) =>
        element.SetValue(ForwardToScrollViewerProperty, value);

    private static void OnForwardToScrollViewerChanged(Control control, AvaloniaPropertyChangedEventArgs args)
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
        if (sender is not Visual visual)
        {
            return;
        }

        var scrollViewer = visual.FindAncestorOfType<ScrollViewer>();
        if (scrollViewer is null)
        {
            return;
        }

        var verticalLines = (int)Math.Round(Math.Abs(e.Delta.Y) * LinesPerWheelNotch);
        for (var i = 0; i < verticalLines; i++)
        {
            if (e.Delta.Y > 0)
            {
                scrollViewer.LineUp();
            }
            else
            {
                scrollViewer.LineDown();
            }
        }

        var horizontalLines = (int)Math.Round(Math.Abs(e.Delta.X) * LinesPerWheelNotch);
        for (var i = 0; i < horizontalLines; i++)
        {
            if (e.Delta.X > 0)
            {
                scrollViewer.LineLeft();
            }
            else
            {
                scrollViewer.LineRight();
            }
        }

        e.Handled = true;
    }
}
