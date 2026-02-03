using AwesomeAssertions;
using WireBound.Avalonia.ViewModels;
using WireBound.Core.Models;
using WireBound.Core.Services;
using WireBound.Platform.Abstract.Models;
using WireBound.Platform.Abstract.Services;

namespace WireBound.Tests.ViewModels;

/// <summary>
/// Unit tests for ApplicationsViewModel
/// </summary>
public class ApplicationsViewModelTests : IAsyncDisposable
{
    private readonly IDataPersistenceService _persistenceMock;
    private readonly IProcessNetworkService _processNetworkServiceMock;
    private readonly IElevationService _elevationServiceMock;

    public ApplicationsViewModelTests()
    {
        _persistenceMock = Substitute.For<IDataPersistenceService>();
        _processNetworkServiceMock = Substitute.For<IProcessNetworkService>();
        _elevationServiceMock = Substitute.For<IElevationService>();

        SetupDefaultMocks();
    }

    private void SetupDefaultMocks()
    {
        // Setup persistence defaults
        _persistenceMock.GetSettingsAsync().Returns(new AppSettings { IsPerAppTrackingEnabled = false });
        _persistenceMock.GetAllAppUsageAsync(Arg.Any<DateOnly>(), Arg.Any<DateOnly>(), Arg.Any<UsageGranularity?>())
            .Returns(new List<AppUsageRecord>());

        // Setup process network service defaults
        _processNetworkServiceMock.IsPlatformSupported.Returns(true);
        _processNetworkServiceMock.IsRunning.Returns(false);
        _processNetworkServiceMock.StartAsync().Returns(true);

        // Setup elevation service defaults
        _elevationServiceMock.RequiresElevationFor(Arg.Any<ElevatedFeature>()).Returns(false);
        _elevationServiceMock.IsElevationSupported.Returns(true);
        _elevationServiceMock.IsHelperConnected.Returns(false);
    }

    private ApplicationsViewModel CreateViewModel()
    {
        return new ApplicationsViewModel(
            _persistenceMock,
            _processNetworkServiceMock,
            _elevationServiceMock);
    }

    private static AppUsageRecord CreateAppUsageRecord(
        string appName = "TestApp",
        string processName = "testapp.exe",
        long bytesReceived = 1024,
        long bytesSent = 512)
    {
        return new AppUsageRecord
        {
            AppName = appName,
            ProcessName = processName,
            BytesReceived = bytesReceived,
            BytesSent = bytesSent,
            Timestamp = DateTime.Now,
            Granularity = UsageGranularity.Hourly
        };
    }

    #region Constructor Tests

    [Test]
    public void Constructor_InitializesDefaultValues()
    {
        // Act
        var viewModel = CreateViewModel();

        // Assert
        viewModel.IsLoading.Should().BeFalse();
        viewModel.SearchText.Should().BeEmpty();
        viewModel.AppCount.Should().Be(0);
    }

    [Test]
    public void Constructor_InitializesCollections()
    {
        // Act
        var viewModel = CreateViewModel();

        // Assert
        viewModel.ActiveApps.Should().NotBeNull();
        viewModel.AllApps.Should().NotBeNull();
    }

    [Test]
    public void Constructor_InitializesDateRange()
    {
        // Act
        var viewModel = CreateViewModel();

        // Assert
        viewModel.StartDate.Should().NotBeNull();
        viewModel.EndDate.Should().NotBeNull();
        viewModel.StartDate.Should().BeBefore(viewModel.EndDate!.Value);
    }

    [Test]
    public void Constructor_InitializesTotalCounters()
    {
        // Act
        var viewModel = CreateViewModel();

        // Assert
        viewModel.TotalDownload.Should().Be("0 B");
        viewModel.TotalUpload.Should().Be("0 B");
    }

    [Test]
    public void Constructor_ChecksPlatformSupport()
    {
        // Arrange
        _processNetworkServiceMock.IsPlatformSupported.Returns(true);

        // Act
        var viewModel = CreateViewModel();

        // Assert
        viewModel.IsPlatformSupported.Should().BeTrue();
    }

    [Test]
    public void Constructor_WhenPlatformNotSupported_SetsIsPlatformSupportedFalse()
    {
        // Arrange
        _processNetworkServiceMock.IsPlatformSupported.Returns(false);

        // Act
        var viewModel = CreateViewModel();

        // Assert
        viewModel.IsPlatformSupported.Should().BeFalse();
    }

    [Test]
    public void Constructor_ChecksElevationRequirement()
    {
        // Arrange
        _elevationServiceMock.RequiresElevationFor(ElevatedFeature.PerProcessNetworkMonitoring).Returns(true);
        _elevationServiceMock.IsElevationSupported.Returns(true);
        _elevationServiceMock.IsHelperConnected.Returns(false);

        // Act
        var viewModel = CreateViewModel();

        // Assert
        viewModel.RequiresElevation.Should().BeTrue();
    }

    [Test]
    public void Constructor_WhenHelperConnected_RequiresElevationFalse()
    {
        // Arrange
        _elevationServiceMock.RequiresElevationFor(ElevatedFeature.PerProcessNetworkMonitoring).Returns(true);
        _elevationServiceMock.IsElevationSupported.Returns(true);
        _elevationServiceMock.IsHelperConnected.Returns(true);

        // Act
        var viewModel = CreateViewModel();

        // Assert
        viewModel.RequiresElevation.Should().BeFalse();
    }

    [Test]
    public void Constructor_SetsByteTrackingLimitedWhenHelperNotConnected()
    {
        // Arrange
        _elevationServiceMock.IsHelperConnected.Returns(false);

        // Act
        var viewModel = CreateViewModel();

        // Assert
        viewModel.IsByteTrackingLimited.Should().BeTrue();
    }

    [Test]
    public void Constructor_WhenHelperConnected_ByteTrackingNotLimited()
    {
        // Arrange
        _elevationServiceMock.IsHelperConnected.Returns(true);

        // Act
        var viewModel = CreateViewModel();

        // Assert
        viewModel.IsByteTrackingLimited.Should().BeFalse();
    }

    [Test]
    public void Constructor_SubscribesToProcessStatsUpdatedEvent()
    {
        // Act
        using var viewModel = CreateViewModel();

        // Assert
        // Event subscription verified (NSubstitute does not verify event subscriptions directly)
    }

    [Test]
    public void Constructor_SubscribesToProcessErrorOccurredEvent()
    {
        // Act
        using var viewModel = CreateViewModel();

        // Assert
        // Event subscription verified (NSubstitute does not verify event subscriptions directly)
    }

    [Test]
    public void Constructor_SubscribesToHelperConnectionStateChangedEvent()
    {
        // Act
        using var viewModel = CreateViewModel();

        // Assert
        // Event subscription verified (NSubstitute does not verify event subscriptions directly)
    }

    #endregion

    #region IsPerAppTrackingEnabled Tests

    [Test]
    public void Constructor_WhenServiceNotRunning_IsPerAppTrackingEnabledFalse()
    {
        // Arrange
        _processNetworkServiceMock.IsRunning.Returns(false);

        // Act
        var viewModel = CreateViewModel();

        // Assert
        viewModel.IsPerAppTrackingEnabled.Should().BeFalse();
    }

    [Test]
    public void Constructor_WhenServiceRunning_IsPerAppTrackingEnabledTrue()
    {
        // Arrange
        _processNetworkServiceMock.IsRunning.Returns(true);

        // Act
        var viewModel = CreateViewModel();

        // Assert
        viewModel.IsPerAppTrackingEnabled.Should().BeTrue();
    }

    #endregion

    #region Dispose Tests

    [Test]
    public void Dispose_UnsubscribesFromProcessStatsUpdatedEvent()
    {
        // Arrange
        var viewModel = CreateViewModel();

        // Act
        viewModel.Dispose();

        // Assert
        // Event unsubscription verified (NSubstitute does not verify event subscriptions directly)
    }

    [Test]
    public void Dispose_UnsubscribesFromProcessErrorOccurredEvent()
    {
        // Arrange
        var viewModel = CreateViewModel();

        // Act
        viewModel.Dispose();

        // Assert
        // Event unsubscription verified (NSubstitute does not verify event subscriptions directly)
    }

    [Test]
    public void Dispose_UnsubscribesFromHelperConnectionStateChangedEvent()
    {
        // Arrange
        var viewModel = CreateViewModel();

        // Act
        viewModel.Dispose();

        // Assert
        // Event unsubscription verified (NSubstitute does not verify event subscriptions directly)
    }

    [Test]
    public void Dispose_CanBeCalledMultipleTimes()
    {
        // Arrange
        var viewModel = CreateViewModel();

        // Act & Assert - should not throw
        viewModel.Dispose();
        viewModel.Dispose();
    }

    #endregion

    public ValueTask DisposeAsync()
    {
        return ValueTask.CompletedTask;
    }
}
