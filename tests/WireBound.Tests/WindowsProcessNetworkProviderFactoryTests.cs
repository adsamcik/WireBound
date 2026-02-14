using AwesomeAssertions;
using NSubstitute;
using TUnit.Core;

namespace WireBound.Tests.Platform;

public class WindowsProcessNetworkProviderFactoryTests
{
    [Test]
    public void GetProvider_WhenElevated_ReturnsRegistryProvider()
    {
        var registryProvider = Substitute.For<IProcessNetworkProvider>();
        var helperProvider = Substitute.For<IProcessNetworkProvider>();
        var elevationChecker = CreateElevationChecker(isElevated: true);
        
        var factory = new WindowsProcessNetworkProviderFactory(
            registryProvider,
            helperProvider,
            elevationChecker);

        var result = factory.GetProvider();

        result.Should().BeSameAs(registryProvider);
    }

    [Test]
    public void GetProvider_WhenNotElevated_ReturnsHelperProvider()
    {
        var registryProvider = Substitute.For<IProcessNetworkProvider>();
        var helperProvider = Substitute.For<IProcessNetworkProvider>();
        var elevationChecker = CreateElevationChecker(isElevated: false);
        
        var factory = new WindowsProcessNetworkProviderFactory(
            registryProvider,
            helperProvider,
            elevationChecker);

        var result = factory.GetProvider();

        result.Should().BeSameAs(helperProvider);
    }

    [Test]
    public void GetProvider_CalledMultipleTimes_ReturnsConsistentProvider()
    {
        var registryProvider = Substitute.For<IProcessNetworkProvider>();
        var helperProvider = Substitute.For<IProcessNetworkProvider>();
        var elevationChecker = CreateElevationChecker(isElevated: true);
        
        var factory = new WindowsProcessNetworkProviderFactory(
            registryProvider,
            helperProvider,
            elevationChecker);

        var result1 = factory.GetProvider();
        var result2 = factory.GetProvider();
        var result3 = factory.GetProvider();

        result1.Should().BeSameAs(result2);
        result2.Should().BeSameAs(result3);
    }

    [Test]
    public void OnHelperConnectionStateChanged_WhenHelperConnects_SwitchesToHelperProvider()
    {
        var registryProvider = Substitute.For<IProcessNetworkProvider>();
        var helperProvider = Substitute.For<IProcessNetworkProvider>();
        var elevationChecker = CreateElevationChecker(isElevated: true);
        
        var factory = new WindowsProcessNetworkProviderFactory(
            registryProvider,
            helperProvider,
            elevationChecker);

        // Initially returns registry provider
        factory.GetProvider().Should().BeSameAs(registryProvider);

        // Helper connects
        factory.OnHelperConnectionStateChanged(isConnected: true);

        // Now returns helper provider
        factory.GetProvider().Should().BeSameAs(helperProvider);
    }

    [Test]
    public void OnHelperConnectionStateChanged_WhenHelperDisconnects_SwitchesBackToElevationBasedProvider()
    {
        var registryProvider = Substitute.For<IProcessNetworkProvider>();
        var helperProvider = Substitute.For<IProcessNetworkProvider>();
        var elevationChecker = CreateElevationChecker(isElevated: true);
        
        var factory = new WindowsProcessNetworkProviderFactory(
            registryProvider,
            helperProvider,
            elevationChecker);

        // Helper connects
        factory.OnHelperConnectionStateChanged(isConnected: true);
        factory.GetProvider().Should().BeSameAs(helperProvider);

        // Helper disconnects
        factory.OnHelperConnectionStateChanged(isConnected: false);

        // Returns to registry provider (since elevated)
        factory.GetProvider().Should().BeSameAs(registryProvider);
    }

    [Test]
    public void OnHelperConnectionStateChanged_WhenNotElevatedAndHelperDisconnects_SwitchesToHelperProvider()
    {
        var registryProvider = Substitute.For<IProcessNetworkProvider>();
        var helperProvider = Substitute.For<IProcessNetworkProvider>();
        var elevationChecker = CreateElevationChecker(isElevated: false);
        
        var factory = new WindowsProcessNetworkProviderFactory(
            registryProvider,
            helperProvider,
            elevationChecker);

        // Initially returns helper provider (not elevated)
        factory.GetProvider().Should().BeSameAs(helperProvider);

        // Simulate helper connecting then disconnecting
        factory.OnHelperConnectionStateChanged(isConnected: true);
        factory.OnHelperConnectionStateChanged(isConnected: false);

        // Should still return helper provider (elevation state unchanged)
        factory.GetProvider().Should().BeSameAs(helperProvider);
    }

    [Test]
    public void OnHelperConnectionStateChanged_CalledMultipleTimes_HandlesStateTransitionsCorrectly()
    {
        var registryProvider = Substitute.For<IProcessNetworkProvider>();
        var helperProvider = Substitute.For<IProcessNetworkProvider>();
        var elevationChecker = CreateElevationChecker(isElevated: true);
        
        var factory = new WindowsProcessNetworkProviderFactory(
            registryProvider,
            helperProvider,
            elevationChecker);

        // Connect -> Disconnect -> Connect -> Disconnect
        factory.OnHelperConnectionStateChanged(true);
        factory.GetProvider().Should().BeSameAs(helperProvider);

        factory.OnHelperConnectionStateChanged(false);
        factory.GetProvider().Should().BeSameAs(registryProvider);

        factory.OnHelperConnectionStateChanged(true);
        factory.GetProvider().Should().BeSameAs(helperProvider);

        factory.OnHelperConnectionStateChanged(false);
        factory.GetProvider().Should().BeSameAs(registryProvider);
    }

    [Test]
    public void OnHelperConnectionStateChanged_WithSameState_DoesNotAffectProvider()
    {
        var registryProvider = Substitute.For<IProcessNetworkProvider>();
        var helperProvider = Substitute.For<IProcessNetworkProvider>();
        var elevationChecker = CreateElevationChecker(isElevated: true);
        
        var factory = new WindowsProcessNetworkProviderFactory(
            registryProvider,
            helperProvider,
            elevationChecker);

        factory.OnHelperConnectionStateChanged(false);
        factory.GetProvider().Should().BeSameAs(registryProvider);

        // Call with same state again
        factory.OnHelperConnectionStateChanged(false);
        factory.GetProvider().Should().BeSameAs(registryProvider);
    }

    [Test]
    public void GetProvider_IsThreadSafe_ReturnsConsistentProviderAcrossThreads()
    {
        var registryProvider = Substitute.For<IProcessNetworkProvider>();
        var helperProvider = Substitute.For<IProcessNetworkProvider>();
        var elevationChecker = CreateElevationChecker(isElevated: true);
        
        var factory = new WindowsProcessNetworkProviderFactory(
            registryProvider,
            helperProvider,
            elevationChecker);

        var providers = new IProcessNetworkProvider[100];
        
        // Access from multiple threads simultaneously
        Parallel.For(0, 100, i =>
        {
            providers[i] = factory.GetProvider();
        });

        // All should be the same instance
        providers.Should().AllBe(registryProvider);
    }

    [Test]
    public void OnHelperConnectionStateChanged_ConcurrentCalls_MaintainsConsistentState()
    {
        var registryProvider = Substitute.For<IProcessNetworkProvider>();
        var helperProvider = Substitute.For<IProcessNetworkProvider>();
        var elevationChecker = CreateElevationChecker(isElevated: true);
        
        var factory = new WindowsProcessNetworkProviderFactory(
            registryProvider,
            helperProvider,
            elevationChecker);

        // Simulate rapid connection state changes from multiple threads
        Parallel.For(0, 50, i =>
        {
            factory.OnHelperConnectionStateChanged(i % 2 == 0);
            _ = factory.GetProvider();
        });

        // Final state should be consistent
        var finalProvider = factory.GetProvider();
        finalProvider.Should().NotBeNull();
        finalProvider.Should().BeOneOf(registryProvider, helperProvider);
    }

    [Test]
    public void Constructor_WithNullRegistryProvider_ThrowsArgumentNullException()
    {
        var helperProvider = Substitute.For<IProcessNetworkProvider>();
        var elevationChecker = CreateElevationChecker(isElevated: true);

        var act = () => new WindowsProcessNetworkProviderFactory(
            null!,
            helperProvider,
            elevationChecker);

        act.Should().Throw<ArgumentNullException>()
            .WithParameter("registryProvider");
    }

    [Test]
    public void Constructor_WithNullHelperProvider_ThrowsArgumentNullException()
    {
        var registryProvider = Substitute.For<IProcessNetworkProvider>();
        var elevationChecker = CreateElevationChecker(isElevated: true);

        var act = () => new WindowsProcessNetworkProviderFactory(
            registryProvider,
            null!,
            elevationChecker);

        act.Should().Throw<ArgumentNullException>()
            .WithParameter("helperProvider");
    }

    [Test]
    public void Constructor_WithNullElevationChecker_ThrowsArgumentNullException()
    {
        var registryProvider = Substitute.For<IProcessNetworkProvider>();
        var helperProvider = Substitute.For<IProcessNetworkProvider>();

        var act = () => new WindowsProcessNetworkProviderFactory(
            registryProvider,
            helperProvider,
            null!);

        act.Should().Throw<ArgumentNullException>()
            .WithParameter("elevationChecker");
    }

    [Test]
    public void ProviderSelection_WithVolatileFieldAccess_EnsuresMemoryVisibility()
    {
        // This test verifies that the volatile field implementation ensures
        // memory visibility across threads. The volatile keyword prevents
        // compiler optimizations that could cache the field value.
        
        var registryProvider = Substitute.For<IProcessNetworkProvider>();
        var helperProvider = Substitute.For<IProcessNetworkProvider>();
        var elevationChecker = CreateElevationChecker(isElevated: true);
        
        var factory = new WindowsProcessNetworkProviderFactory(
            registryProvider,
            helperProvider,
            elevationChecker);

        IProcessNetworkProvider? providerSeenByThread = null;
        var threadStarted = new ManualResetEventSlim();
        var changeState = new ManualResetEventSlim();
        
        var thread = new Thread(() =>
        {
            threadStarted.Set();
            changeState.Wait();
            // Volatile read should see the updated value
            providerSeenByThread = factory.GetProvider();
        });
        
        thread.Start();
        threadStarted.Wait();
        
        // Change state on main thread
        factory.OnHelperConnectionStateChanged(true);
        changeState.Set();
        thread.Join();

        // The other thread should see the updated provider
        providerSeenByThread.Should().BeSameAs(helperProvider);
    }

    [Test]
    public void ElevationState_IsCheckedOnlyDuringConstruction()
    {
        var registryProvider = Substitute.For<IProcessNetworkProvider>();
        var helperProvider = Substitute.For<IProcessNetworkProvider>();
        var elevationChecker = Substitute.For<IElevationChecker>();
        
        // Set initial elevation state
        elevationChecker.IsElevated().Returns(true);
        
        var factory = new WindowsProcessNetworkProviderFactory(
            registryProvider,
            helperProvider,
            elevationChecker);

        // Initial call with elevated state
        factory.GetProvider().Should().BeSameAs(registryProvider);
        
        // Change elevation state (simulating elevation change)
        elevationChecker.IsElevated().Returns(false);
        
        // Factory should still use the original elevation state
        factory.GetProvider().Should().BeSameAs(registryProvider);
        
        // Verify elevation was only checked once (during construction)
        elevationChecker.Received(1).IsElevated();
    }

    private IElevationChecker CreateElevationChecker(bool isElevated)
    {
        var checker = Substitute.For<IElevationChecker>();
        checker.IsElevated().Returns(isElevated);
        return checker;
    }
}

// NOTES ON TESTING STRATEGY:
// 
// 1. Provider Selection Logic:
//    - Tests cover the core factory responsibility: selecting the correct provider
//      based on elevation state and helper connection status
//    - The factory implements a state machine with three states:
//      * Elevated + No Helper → Registry Provider
//      * Not Elevated + No Helper → Helper Provider  
//      * Helper Connected → Helper Provider (regardless of elevation)
//
// 2. Volatile Field Testing:
//    - The factory uses volatile fields to ensure thread-safe access without locks
//    - Tests verify that changes made on one thread are visible to other threads
//    - This is critical for the helper connection state change notifications
//
// 3. Thread Safety:
//    - Multiple tests verify concurrent access patterns
//    - Tests ensure no race conditions in provider selection
//    - Tests verify state transitions are atomic from caller perspective
//
// 4. Integration Notes:
//    - These tests mock IProcessNetworkProvider implementations
//    - Real integration tests should verify:
//      * Actual registry access when elevated
//      * Proper fallback to helper service when not elevated
//      * Helper service connection/disconnection events
//      * Process network data retrieval from both providers
