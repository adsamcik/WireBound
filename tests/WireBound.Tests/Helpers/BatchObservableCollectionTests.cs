using System.Collections.Specialized;
using WireBound.Avalonia.Helpers;

namespace WireBound.Tests.Helpers;

/// <summary>
/// Unit tests for BatchObservableCollection batch operations.
/// </summary>
public class BatchObservableCollectionTests
{
    // ═══════════════════════════════════════════════════════════════════════
    // Constructor Tests
    // ═══════════════════════════════════════════════════════════════════════

    [Test]
    public void Constructor_EmptyCollection_HasZeroCount()
    {
        // Act
        var collection = new BatchObservableCollection<int>();

        // Assert
        collection.Count.Should().Be(0);
    }

    // ═══════════════════════════════════════════════════════════════════════
    // ReplaceAll Tests
    // ═══════════════════════════════════════════════════════════════════════

    [Test]
    public void ReplaceAll_WithArray_ReplacesAllItems()
    {
        // Arrange
        var collection = new BatchObservableCollection<int>();
        var items = new[] { 1, 2, 3, 4, 5 };

        // Act
        collection.ReplaceAll(items);

        // Assert
        collection.Count.Should().Be(5);
        collection[0].Should().Be(1);
        collection[4].Should().Be(5);
    }

    [Test]
    public void ReplaceAll_WithEmptyArray_ClearsCollection()
    {
        // Arrange
        var collection = new BatchObservableCollection<int>();
        collection.Add(1);
        collection.Add(2);
        collection.Add(3);

        // Act
        collection.ReplaceAll(Array.Empty<int>());

        // Assert
        collection.Count.Should().Be(0);
    }

    [Test]
    public void ReplaceAll_FromNonEmpty_ReplacesCorrectly()
    {
        // Arrange
        var collection = new BatchObservableCollection<string>();
        collection.Add("old1");
        collection.Add("old2");

        // Act
        collection.ReplaceAll(new[] { "new1", "new2", "new3" });

        // Assert
        collection.Count.Should().Be(3);
        collection[0].Should().Be("new1");
        collection[1].Should().Be("new2");
        collection[2].Should().Be("new3");
    }

    [Test]
    public void ReplaceAll_FiresSingleResetNotification()
    {
        // Arrange
        var collection = new BatchObservableCollection<int>();
        collection.Add(1);
        collection.Add(2);
        var notifications = new List<NotifyCollectionChangedEventArgs>();
        collection.CollectionChanged += (_, e) => notifications.Add(e);

        // Act
        collection.ReplaceAll(new[] { 10, 20, 30 });

        // Assert
        notifications.Should().HaveCount(1);
        notifications[0].Action.Should().Be(NotifyCollectionChangedAction.Reset);
    }

    [Test]
    public void ReplaceAll_WithIEnumerable_ReplacesAllItems()
    {
        // Arrange
        var collection = new BatchObservableCollection<int>();
        IEnumerable<int> items = Enumerable.Range(1, 5);

        // Act
        collection.ReplaceAll(items);

        // Assert
        collection.Count.Should().Be(5);
        collection[0].Should().Be(1);
        collection[4].Should().Be(5);
    }

    // ═══════════════════════════════════════════════════════════════════════
    // AddRange Tests
    // ═══════════════════════════════════════════════════════════════════════

    [Test]
    public void AddRange_AddsAllItems()
    {
        // Arrange
        var collection = new BatchObservableCollection<int>();

        // Act
        collection.AddRange(new[] { 1, 2, 3 });

        // Assert
        collection.Count.Should().Be(3);
        collection[0].Should().Be(1);
        collection[1].Should().Be(2);
        collection[2].Should().Be(3);
    }

    [Test]
    public void AddRange_EmptyEnumerable_NoChange()
    {
        // Arrange
        var collection = new BatchObservableCollection<int>();
        collection.Add(42);
        var notifications = new List<NotifyCollectionChangedEventArgs>();
        collection.CollectionChanged += (_, e) => notifications.Add(e);

        // Act
        collection.AddRange(Array.Empty<int>());

        // Assert — items unchanged, but Reset notification still fires per implementation
        collection.Count.Should().Be(1);
        collection[0].Should().Be(42);
    }

    [Test]
    public void AddRange_FiresSingleResetNotification()
    {
        // Arrange
        var collection = new BatchObservableCollection<int>();
        var notifications = new List<NotifyCollectionChangedEventArgs>();
        collection.CollectionChanged += (_, e) => notifications.Add(e);

        // Act
        collection.AddRange(new[] { 1, 2, 3, 4 });

        // Assert
        notifications.Should().HaveCount(1);
        notifications[0].Action.Should().Be(NotifyCollectionChangedAction.Reset);
    }

    [Test]
    public void AddRange_ToExistingCollection_AppendsItems()
    {
        // Arrange
        var collection = new BatchObservableCollection<int>();
        collection.Add(1);
        collection.Add(2);

        // Act
        collection.AddRange(new[] { 3, 4, 5 });

        // Assert
        collection.Count.Should().Be(5);
        collection[0].Should().Be(1);
        collection[1].Should().Be(2);
        collection[2].Should().Be(3);
        collection[3].Should().Be(4);
        collection[4].Should().Be(5);
    }
}
