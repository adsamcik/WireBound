using WireBound.Avalonia.Services;
using WireBound.Core;

namespace WireBound.Tests.Services;

/// <summary>
/// Unit tests for NavigationService
/// </summary>
public class NavigationServiceTests
{
    // ═══════════════════════════════════════════════════════════════════════
    // Default State Tests
    // ═══════════════════════════════════════════════════════════════════════

    [Test]
    public void DefaultCurrentView_IsOverview()
    {
        // Arrange & Act
        var service = new NavigationService();

        // Assert
        service.CurrentView.Should().Be(Routes.Overview);
    }

    // ═══════════════════════════════════════════════════════════════════════
    // NavigateTo Tests
    // ═══════════════════════════════════════════════════════════════════════

    [Test]
    public void NavigateTo_ChangesCurrentView()
    {
        // Arrange
        var service = new NavigationService();

        // Act
        service.NavigateTo(Routes.Settings);

        // Assert
        service.CurrentView.Should().Be(Routes.Settings);
    }

    [Test]
    public void NavigateTo_FiresNavigationChangedEvent()
    {
        // Arrange
        var service = new NavigationService();
        string? navigatedTo = null;
        service.NavigationChanged += view => navigatedTo = view;

        // Act
        service.NavigateTo(Routes.Charts);

        // Assert
        navigatedTo.Should().Be(Routes.Charts);
    }

    [Test]
    public void NavigateTo_SameView_DoesNotFireEvent()
    {
        // Arrange
        var service = new NavigationService();
        int eventCount = 0;
        service.NavigationChanged += _ => eventCount++;

        // Act - navigate to the default view (Overview)
        service.NavigateTo(Routes.Overview);

        // Assert
        eventCount.Should().Be(0);
    }

    [Test]
    public void NavigateTo_MultipleNavigations_WorkCorrectly()
    {
        // Arrange
        var service = new NavigationService();
        var navigated = new List<string>();
        service.NavigationChanged += view => navigated.Add(view);

        // Act
        service.NavigateTo(Routes.Charts);
        service.NavigateTo(Routes.Settings);
        service.NavigateTo(Routes.Applications);

        // Assert
        service.CurrentView.Should().Be(Routes.Applications);
        navigated.Should().HaveCount(3);
        navigated[0].Should().Be(Routes.Charts);
        navigated[1].Should().Be(Routes.Settings);
        navigated[2].Should().Be(Routes.Applications);
    }

    [Test]
    public void NavigateTo_BackAndForth_WorksCorrectly()
    {
        // Arrange
        var service = new NavigationService();

        // Act
        service.NavigateTo(Routes.Charts);
        service.NavigateTo(Routes.Overview);
        service.NavigateTo(Routes.Charts);

        // Assert
        service.CurrentView.Should().Be(Routes.Charts);
    }
}
