using System.Collections.ObjectModel;
using System.Collections.Specialized;
using System.ComponentModel;

namespace WireBound.Avalonia.Helpers;

/// <summary>
/// ObservableCollection with batch operations that fire a single Reset notification
/// instead of per-item notifications, reducing UI/chart update overhead.
/// </summary>
public class BatchObservableCollection<T> : ObservableCollection<T>
{
    /// <summary>
    /// Replaces all items with the given collection, firing a single Reset notification.
    /// </summary>
    public void ReplaceAll(IEnumerable<T> items)
    {
        CheckReentrancy();
        Items.Clear();
        foreach (var item in items)
            Items.Add(item);
        OnPropertyChanged(new PropertyChangedEventArgs(nameof(Count)));
        OnPropertyChanged(new PropertyChangedEventArgs("Item[]"));
        OnCollectionChanged(new NotifyCollectionChangedEventArgs(NotifyCollectionChangedAction.Reset));
    }

    /// <summary>
    /// Adds multiple items, firing a single Reset notification.
    /// </summary>
    public void AddRange(IEnumerable<T> items)
    {
        CheckReentrancy();
        foreach (var item in items)
            Items.Add(item);
        OnPropertyChanged(new PropertyChangedEventArgs(nameof(Count)));
        OnPropertyChanged(new PropertyChangedEventArgs("Item[]"));
        OnCollectionChanged(new NotifyCollectionChangedEventArgs(NotifyCollectionChangedAction.Reset));
    }
}
