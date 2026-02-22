using AwesomeAssertions;
using WireBound.Core.Services;
using WireBound.Tests.Fixtures;

namespace WireBound.Tests.Helpers;

/// <summary>
/// Unit tests for UiDispatcherPriority enum and SynchronousDispatcher test fixture.
/// </summary>
public class UiDispatcherPriorityTests
{
    // ═══════════════════════════════════════════════════════════════════════════════
    // UiDispatcherPriority Enum Tests
    // ═══════════════════════════════════════════════════════════════════════════════

    [Test]
    public void UiDispatcherPriority_Background_HasLowerValueThanNormal()
    {
        // Assert
        ((int)UiDispatcherPriority.Background).Should().BeLessThan((int)UiDispatcherPriority.Normal);
    }

    [Test]
    public void UiDispatcherPriority_HasTwoValues()
    {
        // Act
        var values = Enum.GetValues<UiDispatcherPriority>();

        // Assert
        values.Should().HaveCount(2);
    }

    // ═══════════════════════════════════════════════════════════════════════════════
    // SynchronousDispatcher Tests
    // ═══════════════════════════════════════════════════════════════════════════════

    [Test]
    public void SynchronousDispatcher_Post_ExecutesAction()
    {
        // Arrange
        var dispatcher = new SynchronousDispatcher();
        var executed = false;

        // Act
        dispatcher.Post(() => executed = true);

        // Assert
        executed.Should().BeTrue();
    }

    [Test]
    public void SynchronousDispatcher_PostWithPriority_ExecutesAction()
    {
        // Arrange
        var dispatcher = new SynchronousDispatcher();
        var backgroundExecuted = false;
        var normalExecuted = false;

        // Act
        dispatcher.Post(() => backgroundExecuted = true, UiDispatcherPriority.Background);
        dispatcher.Post(() => normalExecuted = true, UiDispatcherPriority.Normal);

        // Assert
        backgroundExecuted.Should().BeTrue();
        normalExecuted.Should().BeTrue();
    }

    [Test]
    public async Task SynchronousDispatcher_InvokeAsync_ExecutesAction()
    {
        // Arrange
        var dispatcher = new SynchronousDispatcher();
        var executed = false;

        // Act
        await dispatcher.InvokeAsync(() => executed = true);

        // Assert
        executed.Should().BeTrue();
    }

    [Test]
    public void SynchronousDispatcher_PostWithBackgroundPriority_ExecutesSynchronously()
    {
        // Arrange
        var dispatcher = new SynchronousDispatcher();
        var order = new List<int>();

        // Act — both should execute synchronously in order
        dispatcher.Post(() => order.Add(1), UiDispatcherPriority.Background);
        dispatcher.Post(() => order.Add(2), UiDispatcherPriority.Background);

        // Assert
        order.Should().HaveCount(2);
        order[0].Should().Be(1);
        order[1].Should().Be(2);
    }

    [Test]
    public void SynchronousDispatcher_ImplementsIUiDispatcher()
    {
        // Arrange & Act
        var dispatcher = new SynchronousDispatcher();

        // Assert
        dispatcher.Should().BeAssignableTo<IUiDispatcher>();
    }
}
