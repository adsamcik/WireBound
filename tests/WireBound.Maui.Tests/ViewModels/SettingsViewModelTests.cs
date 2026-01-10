using FluentAssertions;
using NSubstitute;
using TUnit.Core;
using WireBound.Maui.Models;
using WireBound.Maui.Services;
using WireBound.Maui.ViewModels;

namespace WireBound.Maui.Tests.ViewModels;

public class SettingsViewModelTests
{
    private IDataPersistenceService _persistenceMock = null!;
    private INetworkMonitorService _networkMonitorMock = null!;
    private INetworkPollingBackgroundService _pollingMock = null!;

    [Before(Test)]
    public async Task Setup()
    {
        _persistenceMock = Substitute.For<IDataPersistenceService>();
        _networkMonitorMock = Substitute.For<INetworkMonitorService>();
        _pollingMock = Substitute.For<INetworkPollingBackgroundService>();

        // Setup default settings return
        _persistenceMock.GetSettingsAsync().Returns(new AppSettings
        {
            PollingIntervalMs = 1000,
            SaveIntervalSeconds = 60,
            StartWithWindows = false,
            MinimizeToTray = true,
            UseIpHelperApi = false,
            DataRetentionDays = 365,
            Theme = "Dark",
            SelectedAdapterId = ""
        });

        await Task.CompletedTask;
    }

    [Test]
    public async Task Constructor_ShouldInitializeDefaultValues()
    {
        // Arrange & Act
        var viewModel = new SettingsViewModel(_persistenceMock, _networkMonitorMock, _pollingMock);

        // Assert
        viewModel.PollingIntervalMs.Should().Be(1000);
        viewModel.SaveIntervalSeconds.Should().Be(60);
        viewModel.StartWithWindows.Should().BeFalse();
        viewModel.MinimizeToTray.Should().BeTrue();
        viewModel.UseIpHelperApi.Should().BeFalse();
        viewModel.DataRetentionDays.Should().Be(365);
        viewModel.SelectedTheme.Should().Be("Dark");

        await Task.CompletedTask;
    }

    [Test]
    public async Task Themes_ShouldContainExpectedOptions()
    {
        // Arrange & Act
        var viewModel = new SettingsViewModel(_persistenceMock, _networkMonitorMock, _pollingMock);

        // Assert
        viewModel.Themes.Should().NotBeNull();
        viewModel.Themes.Should().Contain("Light");
        viewModel.Themes.Should().Contain("Dark");
        viewModel.Themes.Should().Contain("System");
        viewModel.Themes.Should().HaveCount(3);

        await Task.CompletedTask;
    }

    [Test]
    public async Task PollingIntervals_ShouldContainExpectedOptions()
    {
        // Arrange & Act
        var viewModel = new SettingsViewModel(_persistenceMock, _networkMonitorMock, _pollingMock);

        // Assert
        viewModel.PollingIntervals.Should().NotBeNull();
        viewModel.PollingIntervals.Should().Contain(500);
        viewModel.PollingIntervals.Should().Contain(1000);
        viewModel.PollingIntervals.Should().Contain(2000);
        viewModel.PollingIntervals.Should().Contain(5000);

        await Task.CompletedTask;
    }

    [Test]
    public async Task SaveIntervals_ShouldContainExpectedOptions()
    {
        // Arrange & Act
        var viewModel = new SettingsViewModel(_persistenceMock, _networkMonitorMock, _pollingMock);

        // Assert
        viewModel.SaveIntervals.Should().NotBeNull();
        viewModel.SaveIntervals.Should().Contain(30);
        viewModel.SaveIntervals.Should().Contain(60);
        viewModel.SaveIntervals.Should().Contain(120);
        viewModel.SaveIntervals.Should().Contain(300);

        await Task.CompletedTask;
    }

    [Test]
    public async Task Adapters_ShouldBeInitialized()
    {
        // Arrange & Act
        var viewModel = new SettingsViewModel(_persistenceMock, _networkMonitorMock, _pollingMock);

        // Assert
        viewModel.Adapters.Should().NotBeNull();

        await Task.CompletedTask;
    }

    [Test]
    public async Task StatusMessage_ShouldBeInitializedEmpty()
    {
        // Arrange & Act
        var viewModel = new SettingsViewModel(_persistenceMock, _networkMonitorMock, _pollingMock);

        // Assert
        viewModel.StatusMessage.Should().Be(string.Empty);

        await Task.CompletedTask;
    }

    [Test]
    public async Task Properties_ShouldBeSettable()
    {
        // Arrange
        var viewModel = new SettingsViewModel(_persistenceMock, _networkMonitorMock, _pollingMock);

        // Act
        viewModel.PollingIntervalMs = 2000;
        viewModel.SaveIntervalSeconds = 120;
        viewModel.StartWithWindows = true;
        viewModel.MinimizeToTray = false;
        viewModel.UseIpHelperApi = true;
        viewModel.DataRetentionDays = 180;
        viewModel.SelectedTheme = "Light";

        // Assert
        viewModel.PollingIntervalMs.Should().Be(2000);
        viewModel.SaveIntervalSeconds.Should().Be(120);
        viewModel.StartWithWindows.Should().BeTrue();
        viewModel.MinimizeToTray.Should().BeFalse();
        viewModel.UseIpHelperApi.Should().BeTrue();
        viewModel.DataRetentionDays.Should().Be(180);
        viewModel.SelectedTheme.Should().Be("Light");

        await Task.CompletedTask;
    }

    [Test]
    public async Task Dispose_ShouldNotThrow()
    {
        // Arrange
        var viewModel = new SettingsViewModel(_persistenceMock, _networkMonitorMock, _pollingMock);

        // Act
        var act = () => viewModel.Dispose();

        // Assert
        act.Should().NotThrow();

        await Task.CompletedTask;
    }
}
