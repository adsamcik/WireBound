using AwesomeAssertions;
using NSubstitute;
using TUnit.Core;
using WireBound.Platform.Abstract.Services;
using WireBound.Platform.Windows.Services;

namespace WireBound.Tests.Platform;

public class WindowsProcessNetworkProviderFactoryTests
{
    [Test]
    public void Constructor_WithNullElevationService_DoesNotThrow()
    {
        var act = () => new WindowsProcessNetworkProviderFactory(elevationService: null);

        act.Should().NotThrow();
    }

    [Test]
    public void HasElevatedProvider_WhenNoElevationService_ReturnsFalse()
    {
        var factory = new WindowsProcessNetworkProviderFactory(elevationService: null);

        factory.HasElevatedProvider.Should().BeFalse();
    }

    [Test]
    public void HasElevatedProvider_WhenHelperNotConnected_ReturnsFalse()
    {
        var elevationService = Substitute.For<IElevationService>();
        elevationService.IsHelperConnected.Returns(false);

        var factory = new WindowsProcessNetworkProviderFactory(elevationService);

        factory.HasElevatedProvider.Should().BeFalse();
    }

    [Test]
    public void HasElevatedProvider_WhenHelperConnected_ReturnsTrue()
    {
        var elevationService = Substitute.For<IElevationService>();
        elevationService.IsHelperConnected.Returns(true);

        var factory = new WindowsProcessNetworkProviderFactory(elevationService);

        factory.HasElevatedProvider.Should().BeTrue();
    }

    [Test]
    public void GetProvider_WhenNoElevationService_ReturnsNonNullProvider()
    {
        var factory = new WindowsProcessNetworkProviderFactory(elevationService: null);

        var provider = factory.GetProvider();

        provider.Should().NotBeNull();
    }

    [Test]
    public void GetProvider_CalledMultipleTimes_ReturnsSameBasicProvider()
    {
        var factory = new WindowsProcessNetworkProviderFactory(elevationService: null);

        var provider1 = factory.GetProvider();
        var provider2 = factory.GetProvider();

        provider1.Should().BeSameAs(provider2);
    }

    [Test]
    public void ProviderChanged_WhenHelperConnects_FiresEvent()
    {
        var elevationService = Substitute.For<IElevationService>();
        elevationService.IsHelperConnected.Returns(false);
        var helperConnection = Substitute.For<IHelperConnection>();
        elevationService.GetHelperConnection().Returns(helperConnection);

        var factory = new WindowsProcessNetworkProviderFactory(elevationService);

        var eventFired = false;
        factory.ProviderChanged += (_, _) => eventFired = true;

        // Simulate helper connecting: update IsHelperConnected, then raise event
        elevationService.IsHelperConnected.Returns(true);
        elevationService.HelperConnectionStateChanged += Raise.Event<EventHandler<HelperConnectionStateChangedEventArgs>>(
            elevationService,
            new HelperConnectionStateChangedEventArgs(true, "connected"));

        eventFired.Should().BeTrue();
    }

    [Test]
    public void ProviderChanged_WhenHelperDisconnects_FiresEvent()
    {
        var elevationService = Substitute.For<IElevationService>();
        elevationService.IsHelperConnected.Returns(true);
        var helperConnection = Substitute.For<IHelperConnection>();
        elevationService.GetHelperConnection().Returns(helperConnection);

        var factory = new WindowsProcessNetworkProviderFactory(elevationService);

        // First connect so there's an elevated provider to disconnect from
        elevationService.HelperConnectionStateChanged += Raise.Event<EventHandler<HelperConnectionStateChangedEventArgs>>(
            elevationService,
            new HelperConnectionStateChangedEventArgs(true, "connected"));

        var eventFired = false;
        factory.ProviderChanged += (_, _) => eventFired = true;

        elevationService.IsHelperConnected.Returns(false);
        elevationService.HelperConnectionStateChanged += Raise.Event<EventHandler<HelperConnectionStateChangedEventArgs>>(
            elevationService,
            new HelperConnectionStateChangedEventArgs(false, "disconnected"));

        eventFired.Should().BeTrue();
    }

    [Test]
    public void GetProvider_AfterHelperDisconnects_ReturnsBasicProvider()
    {
        var elevationService = Substitute.For<IElevationService>();
        elevationService.IsHelperConnected.Returns(false);

        var factory = new WindowsProcessNetworkProviderFactory(elevationService);
        var basicProvider = factory.GetProvider();

        // Disconnect event (no elevated provider was set)
        elevationService.HelperConnectionStateChanged += Raise.Event<EventHandler<HelperConnectionStateChangedEventArgs>>(
            elevationService,
            new HelperConnectionStateChangedEventArgs(false, "disconnected"));

        factory.GetProvider().Should().BeSameAs(basicProvider);
    }
}
