using AwesomeAssertions;
using WireBound.Core.Helpers;

namespace WireBound.Tests.Helpers;

/// <summary>
/// Unit tests for CircularBuffer data structure
/// </summary>
public class CircularBufferTests
{
    // ═══════════════════════════════════════════════════════════════════════
    // Constructor Tests
    // ═══════════════════════════════════════════════════════════════════════

    [Fact]
    public void Constructor_ValidCapacity_CreatesBuffer()
    {
        // Act
        var buffer = new CircularBuffer<int>(10);

        // Assert
        buffer.Capacity.Should().Be(10);
        buffer.Count.Should().Be(0);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    [InlineData(-100)]
    public void Constructor_InvalidCapacity_ThrowsArgumentOutOfRangeException(int capacity)
    {
        // Act & Assert
        var action = () => new CircularBuffer<int>(capacity);
        action.Should().Throw<ArgumentOutOfRangeException>();
    }

    [Fact]
    public void Constructor_MinimumCapacity_Works()
    {
        // Act
        var buffer = new CircularBuffer<int>(1);

        // Assert
        buffer.Capacity.Should().Be(1);
        buffer.Count.Should().Be(0);
    }

    // ═══════════════════════════════════════════════════════════════════════
    // Add Tests
    // ═══════════════════════════════════════════════════════════════════════

    [Fact]
    public void Add_SingleItem_IncreasesCount()
    {
        // Arrange
        var buffer = new CircularBuffer<int>(10);

        // Act
        buffer.Add(42);

        // Assert
        buffer.Count.Should().Be(1);
    }

    [Fact]
    public void Add_MultipleItems_IncreasesCount()
    {
        // Arrange
        var buffer = new CircularBuffer<int>(10);

        // Act
        buffer.Add(1);
        buffer.Add(2);
        buffer.Add(3);

        // Assert
        buffer.Count.Should().Be(3);
    }

    [Fact]
    public void Add_FillToCapacity_CountEqualsCapacity()
    {
        // Arrange
        var buffer = new CircularBuffer<int>(5);

        // Act
        for (int i = 0; i < 5; i++)
        {
            buffer.Add(i);
        }

        // Assert
        buffer.Count.Should().Be(5);
    }

    [Fact]
    public void Add_ExceedCapacity_CountStaysAtCapacity()
    {
        // Arrange
        var buffer = new CircularBuffer<int>(5);

        // Act
        for (int i = 0; i < 10; i++)
        {
            buffer.Add(i);
        }

        // Assert
        buffer.Count.Should().Be(5);
    }

    [Fact]
    public void Add_ExceedCapacity_OverwritesOldestItems()
    {
        // Arrange
        var buffer = new CircularBuffer<int>(3);

        // Act
        buffer.Add(1);
        buffer.Add(2);
        buffer.Add(3);
        buffer.Add(4); // Should overwrite 1

        // Assert
        var items = buffer.ToArray();
        items.Should().BeEquivalentTo([2, 3, 4]);
    }

    [Fact]
    public void Add_WrapAround_MaintainsCorrectOrder()
    {
        // Arrange
        var buffer = new CircularBuffer<int>(3);

        // Act
        for (int i = 1; i <= 6; i++)
        {
            buffer.Add(i);
        }

        // Assert - Should contain 4, 5, 6 in order
        var items = buffer.ToArray();
        items.Should().BeEquivalentTo([4, 5, 6]);
    }

    [Fact]
    public void Add_NullValues_Allowed()
    {
        // Arrange
        var buffer = new CircularBuffer<string?>(3);

        // Act
        buffer.Add(null);
        buffer.Add("test");
        buffer.Add(null);

        // Assert
        buffer.Count.Should().Be(3);
        var items = buffer.ToArray();
        items.Should().BeEquivalentTo([null, "test", null]);
    }

    // ═══════════════════════════════════════════════════════════════════════
    // Clear Tests
    // ═══════════════════════════════════════════════════════════════════════

    [Fact]
    public void Clear_EmptyBuffer_DoesNothing()
    {
        // Arrange
        var buffer = new CircularBuffer<int>(10);

        // Act
        buffer.Clear();

        // Assert
        buffer.Count.Should().Be(0);
    }

    [Fact]
    public void Clear_WithItems_ResetsCount()
    {
        // Arrange
        var buffer = new CircularBuffer<int>(10);
        buffer.Add(1);
        buffer.Add(2);
        buffer.Add(3);

        // Act
        buffer.Clear();

        // Assert
        buffer.Count.Should().Be(0);
    }

    [Fact]
    public void Clear_AfterClear_CanAddNewItems()
    {
        // Arrange
        var buffer = new CircularBuffer<int>(3);
        buffer.Add(1);
        buffer.Add(2);
        buffer.Clear();

        // Act
        buffer.Add(10);
        buffer.Add(20);

        // Assert
        buffer.Count.Should().Be(2);
        buffer.ToArray().Should().BeEquivalentTo([10, 20]);
    }

    [Fact]
    public void Clear_PreservesCapacity()
    {
        // Arrange
        var buffer = new CircularBuffer<int>(10);
        buffer.Add(1);
        buffer.Add(2);

        // Act
        buffer.Clear();

        // Assert
        buffer.Capacity.Should().Be(10);
    }

    // ═══════════════════════════════════════════════════════════════════════
    // AsEnumerable / ToArray Tests
    // ═══════════════════════════════════════════════════════════════════════

    [Fact]
    public void ToArray_EmptyBuffer_ReturnsEmptyArray()
    {
        // Arrange
        var buffer = new CircularBuffer<int>(10);

        // Act
        var array = buffer.ToArray();

        // Assert
        array.Should().BeEmpty();
    }

    [Fact]
    public void ToArray_PartiallyFilled_ReturnsOnlyAddedItems()
    {
        // Arrange
        var buffer = new CircularBuffer<int>(10);
        buffer.Add(1);
        buffer.Add(2);
        buffer.Add(3);

        // Act
        var array = buffer.ToArray();

        // Assert
        array.Should().HaveCount(3);
        array.Should().BeEquivalentTo([1, 2, 3]);
    }

    [Fact]
    public void ToArray_FullyFilled_ReturnsAllItems()
    {
        // Arrange
        var buffer = new CircularBuffer<int>(5);
        for (int i = 1; i <= 5; i++)
        {
            buffer.Add(i);
        }

        // Act
        var array = buffer.ToArray();

        // Assert
        array.Should().HaveCount(5);
        array.Should().BeEquivalentTo([1, 2, 3, 4, 5]);
    }

    [Fact]
    public void ToArray_AfterWrapAround_ReturnsItemsInCorrectOrder()
    {
        // Arrange
        var buffer = new CircularBuffer<int>(3);
        buffer.Add(1);
        buffer.Add(2);
        buffer.Add(3);
        buffer.Add(4);
        buffer.Add(5);

        // Act
        var array = buffer.ToArray();

        // Assert - Should be [3, 4, 5] in order (oldest to newest)
        array.Should().BeEquivalentTo([3, 4, 5]);
    }

    [Fact]
    public void AsEnumerable_AllowsLinqOperations()
    {
        // Arrange
        var buffer = new CircularBuffer<int>(5);
        buffer.Add(1);
        buffer.Add(2);
        buffer.Add(3);
        buffer.Add(4);
        buffer.Add(5);

        // Act
        var sum = buffer.AsEnumerable().Sum();
        var avg = buffer.AsEnumerable().Average();

        // Assert
        sum.Should().Be(15);
        avg.Should().Be(3.0);
    }

    [Fact]
    public void AsEnumerable_AfterWrapAround_EnumeratesCorrectly()
    {
        // Arrange
        var buffer = new CircularBuffer<int>(3);
        for (int i = 1; i <= 10; i++)
        {
            buffer.Add(i);
        }

        // Act
        var items = buffer.AsEnumerable().ToList();

        // Assert - Should contain [8, 9, 10]
        items.Should().BeEquivalentTo([8, 9, 10]);
    }

    // ═══════════════════════════════════════════════════════════════════════
    // Edge Cases and Stress Tests
    // ═══════════════════════════════════════════════════════════════════════

    [Fact]
    public void SingleCapacity_WrapAround_WorksCorrectly()
    {
        // Arrange
        var buffer = new CircularBuffer<int>(1);

        // Act
        buffer.Add(1);
        buffer.Add(2);
        buffer.Add(3);

        // Assert
        buffer.Count.Should().Be(1);
        buffer.ToArray().Should().BeEquivalentTo([3]);
    }

    [Fact]
    public void LargeBuffer_ManyItems_MaintainsIntegrity()
    {
        // Arrange
        var buffer = new CircularBuffer<int>(1000);

        // Act - Add 5000 items
        for (int i = 0; i < 5000; i++)
        {
            buffer.Add(i);
        }

        // Assert
        buffer.Count.Should().Be(1000);
        var array = buffer.ToArray();
        array.Should().HaveCount(1000);
        
        // Should contain the last 1000 items: 4000-4999
        array[0].Should().Be(4000);
        array[999].Should().Be(4999);
    }

    [Fact]
    public void ReferenceTypes_WorkCorrectly()
    {
        // Arrange
        var buffer = new CircularBuffer<string>(3);

        // Act
        buffer.Add("first");
        buffer.Add("second");
        buffer.Add("third");
        buffer.Add("fourth");

        // Assert
        var items = buffer.ToArray();
        items.Should().BeEquivalentTo(["second", "third", "fourth"]);
    }

    [Fact]
    public void ComplexTypes_WorkCorrectly()
    {
        // Arrange
        var buffer = new CircularBuffer<(int id, string name)>(2);

        // Act
        buffer.Add((1, "one"));
        buffer.Add((2, "two"));
        buffer.Add((3, "three"));

        // Assert
        var items = buffer.ToArray();
        items.Should().HaveCount(2);
        items[0].Should().Be((2, "two"));
        items[1].Should().Be((3, "three"));
    }
}
