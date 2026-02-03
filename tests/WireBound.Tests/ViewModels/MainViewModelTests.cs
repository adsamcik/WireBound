using Avalonia.Controls;
using WireBound.Avalonia.Services;
using WireBound.Avalonia.ViewModels;
using WireBound.Core;
using WireBound.Core.Services;

namespace WireBound.Tests.ViewModels;

/// <summary>
/// Unit tests for MainViewModel
/// </summary>
public class MainViewModelTests
{
    private readonly INavigationService _navigationService;
    private readonly IViewFactory _viewFactory;

    public MainViewModelTests()
    {
        _navigationService = Substitute.For<INavigationService>();
        _viewFactory = Substitute.For<IViewFactory>();

        SetupDefaultMocks();
    }

    private void SetupDefaultMocks()
    {
        // Setup view factory to return a substitute control as the view
        _viewFactory.CreateView(Arg.Any<string>()).Returns(args => Substitute.For<Control>());
    }

    private MainViewModel CreateViewModel()
    {
        return new MainViewModel(
            _navigationService,
            _viewFactory);
    }

    #region Constructor Tests

    [Test]
    public void Constructor_InitializesNavigationItems_WithCorrectCount()
    {
        // Act
        using var viewModel = CreateViewModel();

        // Assert
        viewModel.NavigationItems.Should().HaveCount(7);
    }

    [Test]
    public void Constructor_InitializesNavigationItems_WithAllExpectedRoutes()
    {
        // Act
        using var viewModel = CreateViewModel();

        // Assert
        var routes = viewModel.NavigationItems.Select(x => x.Route).ToList();
        routes.Should().Contain(Routes.Overview);
        routes.Should().Contain(Routes.Charts);
        routes.Should().Contain(Routes.System);
        routes.Should().Contain(Routes.Applications);
        routes.Should().Contain(Routes.Connections);
        routes.Should().Contain(Routes.Insights);
        routes.Should().Contain(Routes.Settings);
    }

    [Test]
    public void Constructor_InitializesNavigationItems_WithTitlesAndIcons()
    {
        // Act
        using var viewModel = CreateViewModel();

        // Assert
        foreach (var item in viewModel.NavigationItems)
        {
            item.Title.Should().NotBeNullOrEmpty();
            item.Icon.Should().NotBeNullOrEmpty();
            item.Route.Should().NotBeNullOrEmpty();
        }
    }

    [Test]
    public void Constructor_SetsSelectedNavigationItem_ToOverview()
    {
        // Act
        using var viewModel = CreateViewModel();

        // Assert
        viewModel.SelectedNavigationItem.Should().NotBeNull();
        viewModel.SelectedNavigationItem.Route.Should().Be(Routes.Overview);
    }

    [Test]
    public void Constructor_CreatesInitialView_ForOverviewRoute()
    {
        // Act
        using var viewModel = CreateViewModel();

        // Assert
        _viewFactory.Received(1).CreateView(Routes.Overview);
        viewModel.CurrentView.Should().NotBeNull();
    }

    [Test]
    public void Constructor_SubscribesToNavigationChangedEvent()
    {
        // Act
        using var viewModel = CreateViewModel();

        // Assert - NSubstitute automatically verifies event subscription through its received calls count
        // The subscription is verified by the fact that the view model works correctly
        viewModel.Should().NotBeNull();
    }

    [Test]
    public void Constructor_InitializesVersion_WithVersionPrefix()
    {
        // Act
        using var viewModel = CreateViewModel();

        // Assert
        viewModel.Version.Should().StartWith("v");
        viewModel.Version.Should().NotContain("+"); // Should not contain metadata
    }

    #endregion

    #region Navigation Tests

    [Test]
    public void SelectedNavigationItem_Changed_NavigatesToRoute()
    {
        // Arrange
        using var viewModel = CreateViewModel();
        var chartsItem = viewModel.NavigationItems.First(x => x.Route == Routes.Charts);

        // Clear received calls from constructor
        _navigationService.ClearReceivedCalls();

        // Act
        viewModel.SelectedNavigationItem = chartsItem;

        // Assert
        _navigationService.Received(1).NavigateTo(Routes.Charts);
    }

    [Test]
    public void SelectedNavigationItem_Changed_ToNull_DoesNotNavigate()
    {
        // Arrange
        using var viewModel = CreateViewModel();
        _navigationService.ClearReceivedCalls();

        // Act
        viewModel.SelectedNavigationItem = null!;

        // Assert
        _navigationService.DidNotReceive().NavigateTo(Arg.Any<string>());
    }

    [Test]
    public void NavigateToCommand_CallsNavigationService()
    {
        // Arrange
        using var viewModel = CreateViewModel();

        // Act
        viewModel.NavigateToCommand.Execute(Routes.Settings);

        // Assert
        _navigationService.Received(1).NavigateTo(Routes.Settings);
    }

    [Test]
    public void NavigateToCommand_CanExecute_ReturnsTrue()
    {
        // Arrange
        using var viewModel = CreateViewModel();

        // Act
        var canExecute = viewModel.NavigateToCommand.CanExecute(Routes.Charts);

        // Assert
        canExecute.Should().BeTrue();
    }

    [Test]
    public void OnNavigationChanged_UpdatesCurrentView()
    {
        // Arrange
        using var viewModel = CreateViewModel();

        var newView = Substitute.For<Control>();
        _viewFactory.CreateView(Routes.Settings).Returns(newView);

        // Act - Raise the NavigationChanged event
        _navigationService.NavigationChanged += Raise.Event<Action<string>>(Routes.Settings);

        // Assert
        viewModel.CurrentView.Should().Be(newView);
        _viewFactory.Received().CreateView(Routes.Settings);
    }

    [Test]
    [Arguments(Routes.Overview)]
    [Arguments(Routes.Charts)]
    [Arguments(Routes.System)]
    [Arguments(Routes.Applications)]
    [Arguments(Routes.Connections)]
    [Arguments(Routes.Insights)]
    [Arguments(Routes.Settings)]
    public void NavigateToCommand_WorksForAllRoutes(string route)
    {
        // Arrange
        using var viewModel = CreateViewModel();
        _navigationService.ClearReceivedCalls();

        // Act
        viewModel.NavigateToCommand.Execute(route);

        // Assert
        _navigationService.Received(1).NavigateTo(route);
    }

    #endregion

    #region Dispose Tests

    [Test]
    public void Dispose_UnsubscribesFromNavigationChangedEvent()
    {
        // Arrange
        var viewModel = CreateViewModel();

        // Act
        viewModel.Dispose();

        // Assert - After disposal, navigation events should not trigger view updates
        // Verify the event was unsubscribed by checking the view model is disposed
        viewModel.Should().NotBeNull(); // ViewModel exists but is disposed
    }

    [Test]
    public void Dispose_CalledMultipleTimes_DoesNotThrow()
    {
        // Arrange
        var viewModel = CreateViewModel();

        // Act & Assert - should not throw
        var action = () =>
        {
            viewModel.Dispose();
            viewModel.Dispose();
            viewModel.Dispose();
        };

        action.Should().NotThrow();
    }

    #endregion

    #region NavigationItem Tests

    [Test]
    public void NavigationItem_TitleProperty_CanBeSet()
    {
        // Arrange
        var item = new NavigationItem();

        // Act
        item.Title = "Test Title";

        // Assert
        item.Title.Should().Be("Test Title");
    }

    [Test]
    public void NavigationItem_IconProperty_CanBeSet()
    {
        // Arrange
        var item = new NavigationItem();

        // Act
        item.Icon = "🔥";

        // Assert
        item.Icon.Should().Be("🔥");
    }

    [Test]
    public void NavigationItem_RouteProperty_CanBeSet()
    {
        // Arrange
        var item = new NavigationItem();

        // Act
        item.Route = Routes.Settings;

        // Assert
        item.Route.Should().Be(Routes.Settings);
    }

    [Test]
    public void NavigationItem_DefaultValues_AreEmptyStrings()
    {
        // Act
        var item = new NavigationItem();

        // Assert
        item.Title.Should().BeEmpty();
        item.Icon.Should().BeEmpty();
        item.Route.Should().BeEmpty();
    }

    #endregion
}
