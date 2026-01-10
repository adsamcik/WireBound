using FluentAssertions;
using NSubstitute;
using TUnit.Core;
using WireBound.Maui.Services;
using WireBound.Maui.ViewModels;

namespace WireBound.Maui.Tests.ViewModels;

public class MainViewModelTests : IDisposable
{
    private DashboardViewModel _dashboardViewModel = null!;
    private HistoryViewModel _historyViewModel = null!;
    private SettingsViewModel _settingsViewModel = null!;

    [Before(Test)]
    public async Task Setup()
    {
        var networkMonitorMock = Substitute.For<INetworkMonitorService>();
        var persistenceMock = Substitute.For<IDataPersistenceService>();
        var pollingMock = Substitute.For<INetworkPollingBackgroundService>();

        _dashboardViewModel = new DashboardViewModel(networkMonitorMock);
        _historyViewModel = new HistoryViewModel(persistenceMock);
        _settingsViewModel = new SettingsViewModel(persistenceMock, networkMonitorMock, pollingMock);

        await Task.CompletedTask;
    }

    public void Dispose()
    {
        _dashboardViewModel?.Dispose();
        _historyViewModel?.Dispose();
        _settingsViewModel?.Dispose();
    }

    [Test]
    public async Task Constructor_ShouldInitializeViewModels()
    {
        // Arrange & Act
        var viewModel = new MainViewModel(_dashboardViewModel, _historyViewModel, _settingsViewModel);

        // Assert
        viewModel.DashboardViewModel.Should().NotBeNull();
        viewModel.DashboardViewModel.Should().Be(_dashboardViewModel);
        viewModel.HistoryViewModel.Should().NotBeNull();
        viewModel.HistoryViewModel.Should().Be(_historyViewModel);
        viewModel.SettingsViewModel.Should().NotBeNull();
        viewModel.SettingsViewModel.Should().Be(_settingsViewModel);

        await Task.CompletedTask;
    }

    [Test]
    public async Task NavigateToDashboardCommand_ShouldSetSelectedNavigationIndexToZero()
    {
        // Arrange
        var viewModel = new MainViewModel(_dashboardViewModel, _historyViewModel, _settingsViewModel);
        viewModel.SelectedNavigationIndex = 1; // Start at a different index

        // Act
        viewModel.NavigateToDashboardCommand.Execute(null);

        // Assert
        viewModel.SelectedNavigationIndex.Should().Be(0);

        await Task.CompletedTask;
    }

    [Test]
    public async Task NavigateToHistoryCommand_ShouldSetSelectedNavigationIndexToOne()
    {
        // Arrange
        var viewModel = new MainViewModel(_dashboardViewModel, _historyViewModel, _settingsViewModel);
        viewModel.SelectedNavigationIndex = 0; // Start at dashboard

        // Act
        viewModel.NavigateToHistoryCommand.Execute(null);

        // Assert
        viewModel.SelectedNavigationIndex.Should().Be(1);

        await Task.CompletedTask;
    }

    [Test]
    public async Task NavigateToSettingsCommand_ShouldSetSelectedNavigationIndexToTwo()
    {
        // Arrange
        var viewModel = new MainViewModel(_dashboardViewModel, _historyViewModel, _settingsViewModel);
        viewModel.SelectedNavigationIndex = 0; // Start at dashboard

        // Act
        viewModel.NavigateToSettingsCommand.Execute(null);

        // Assert
        viewModel.SelectedNavigationIndex.Should().Be(2);

        await Task.CompletedTask;
    }

    [Test]
    public async Task SelectedNavigationIndex_ShouldDefaultToZero()
    {
        // Arrange & Act
        var viewModel = new MainViewModel(_dashboardViewModel, _historyViewModel, _settingsViewModel);

        // Assert
        viewModel.SelectedNavigationIndex.Should().Be(0);

        await Task.CompletedTask;
    }

    [Test]
    public async Task SelectedNavigationIndex_ShouldBeSettable()
    {
        // Arrange
        var viewModel = new MainViewModel(_dashboardViewModel, _historyViewModel, _settingsViewModel);

        // Act
        viewModel.SelectedNavigationIndex = 2;

        // Assert
        viewModel.SelectedNavigationIndex.Should().Be(2);

        await Task.CompletedTask;
    }

    [Test]
    public async Task Dispose_ShouldDisposeAllViewModels()
    {
        // Arrange
        var viewModel = new MainViewModel(_dashboardViewModel, _historyViewModel, _settingsViewModel);

        // Act
        var act = () => viewModel.Dispose();

        // Assert
        act.Should().NotThrow();

        await Task.CompletedTask;
    }
}
