using System;
using Avalonia.Controls;
using Avalonia.Threading;
using WireBound.Avalonia.ViewModels;

namespace WireBound.Avalonia.Views;

public partial class AppsView : UserControl
{
    private AppsViewModel? _attachedViewModel;

    public AppsView()
    {
        InitializeComponent();
        DataContextChanged += OnDataContextChanged;
        DetachedFromVisualTree += (_, _) => DetachViewModel();
    }

    private void OnDataContextChanged(object? sender, EventArgs e)
    {
        DetachViewModel();
        if (DataContext is AppsViewModel vm)
        {
            vm.ResetListScrollRequested += ScrollListToTop;
            _attachedViewModel = vm;
        }
    }

    private void DetachViewModel()
    {
        if (_attachedViewModel is not null)
        {
            _attachedViewModel.ResetListScrollRequested -= ScrollListToTop;
            _attachedViewModel = null;
        }
    }

    /// <summary>
    /// Post a scroll-to-top onto the UI thread queue. Doing it inline can
    /// race with the layout pass that Apps.ReplaceAll just triggered — the
    /// ListBox's internal ScrollViewer may not yet exist or may not have
    /// re-measured. Posting back via the dispatcher lets the rearrange
    /// settle first.
    /// </summary>
    private void ScrollListToTop()
    {
        Dispatcher.UIThread.Post(() =>
        {
            if (this.FindControl<ListBox>("AppsList") is { Scroll: ScrollViewer scroll })
            {
                scroll.Offset = new global::Avalonia.Vector(scroll.Offset.X, 0);
            }
        }, DispatcherPriority.Background);
    }
}
