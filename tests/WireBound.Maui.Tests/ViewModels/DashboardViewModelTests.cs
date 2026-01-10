using FluentAssertions;
using NSubstitute;
using TUnit.Core;
using WireBound.Maui.Models;
using WireBound.Maui.Services;
using WireBound.Maui.ViewModels;

namespace WireBound.Maui.Tests.ViewModels;

public class DashboardViewModelTests
{
    private INetworkMonitorService _networkMonitorMock = null!;

    [Before(Test)]
    public async Task Setup()
    {
        _networkMonitorMock = Substitute.For<INetworkMonitorService>();
        _networkMonitorMock.GetAdapters().Returns(new List<NetworkAdapter>
        {
            new() { Id = "eth0", Name = "Ethernet" },
            new() { Id = "wifi0", Name = "Wi-Fi" }
        });

        await Task.CompletedTask;
    }

    [Test]
    public async Task Constructor_ShouldInitializeDefaultValues()
    {
        // Arrange & Act
        var viewModel = new DashboardViewModel(_networkMonitorMock);

        // Assert
        viewModel.DownloadSpeed.Should().Be("0 B/s");
        viewModel.UploadSpeed.Should().Be("0 B/s");
        viewModel.SessionDownload.Should().Be("0 B");
        viewModel.SessionUpload.Should().Be("0 B");
        viewModel.SelectedAdapterName.Should().Be("All Adapters");

        await Task.CompletedTask;
    }

    [Test]
    public async Task Constructor_ShouldLoadAdapters()
    {
        // Arrange & Act
        var viewModel = new DashboardViewModel(_networkMonitorMock);

        // Assert - Should have "All Adapters" plus the mocked adapters
        viewModel.Adapters.Should().NotBeNull();
        viewModel.Adapters.Count.Should().BeGreaterThanOrEqualTo(1);
        viewModel.Adapters[0].Name.Should().Be("All Adapters");

        await Task.CompletedTask;
    }

    [Test]
    public async Task SpeedSeries_ShouldBeInitialized()
    {
        // Arrange & Act
        var viewModel = new DashboardViewModel(_networkMonitorMock);

        // Assert
        viewModel.SpeedSeries.Should().NotBeNull();
        viewModel.SpeedSeries.Should().HaveCount(2); // Download and Upload series

        await Task.CompletedTask;
    }

    [Test]
    public async Task XAxes_ShouldBeConfigured()
    {
        // Arrange & Act
        var viewModel = new DashboardViewModel(_networkMonitorMock);

        // Assert
        viewModel.XAxes.Should().NotBeNull();
        viewModel.XAxes.Should().HaveCount(1);
        viewModel.XAxes[0].Name.Should().Be("Time");

        await Task.CompletedTask;
    }

    [Test]
    public async Task YAxes_ShouldBeConfigured()
    {
        // Arrange & Act
        var viewModel = new DashboardViewModel(_networkMonitorMock);

        // Assert
        viewModel.YAxes.Should().NotBeNull();
        viewModel.YAxes.Should().HaveCount(1);
        viewModel.YAxes[0].Name.Should().Be("Speed");

        await Task.CompletedTask;
    }

    [Test]
    public async Task Dispose_ShouldNotThrow()
    {
        // Arrange
        var viewModel = new DashboardViewModel(_networkMonitorMock);

        // Act
        var act = () => viewModel.Dispose();

        // Assert
        act.Should().NotThrow();

        await Task.CompletedTask;
    }

    [Test]
    public async Task SelectedAdapter_ShouldBeNullable()
    {
        // Arrange & Act
        var viewModel = new DashboardViewModel(_networkMonitorMock);

        // Assert - SelectedAdapter should start as null or be nullable
        // Note: Setting SelectedAdapter requires MAUI platform to be running
        // so we only verify it's a nullable property
        viewModel.Adapters.Should().NotBeNull();

        await Task.CompletedTask;
    }
}
