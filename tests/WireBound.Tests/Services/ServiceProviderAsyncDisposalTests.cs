using AwesomeAssertions;
using Microsoft.Extensions.DependencyInjection;

namespace WireBound.Tests.Services;

/// <summary>
/// Regression tests guarding the shutdown invariant in <see cref="WireBound.Avalonia.App"/>.
/// </summary>
/// <remarks>
/// <para>
/// The application registers services that implement only <see cref="IAsyncDisposable"/>
/// (notably <c>WindowsElevationService</c> and <c>LinuxElevationService</c>), because
/// their cleanup is genuinely asynchronous (named-pipe disconnect, semaphore wait,
/// helper-process shutdown).
/// </para>
/// <para>
/// When such a service is registered as a singleton, <see cref="ServiceProvider.Dispose"/>
/// throws <see cref="InvalidOperationException"/>. The shutdown handler in
/// <c>App.axaml.cs</c> must therefore call <see cref="ServiceProvider.DisposeAsync"/>.
/// These tests pin that contract so the regression cannot silently return.
/// </para>
/// </remarks>
public class ServiceProviderAsyncDisposalTests
{
    [Test]
    public void Dispose_OnContainerWithAsyncOnlyDisposableSingleton_Throws()
    {
        var services = new ServiceCollection();
        services.AddSingleton<AsyncOnlyDisposable>();
        var provider = services.BuildServiceProvider();

        _ = provider.GetRequiredService<AsyncOnlyDisposable>();

        var act = () => provider.Dispose();

        act.Should()
            .Throw<InvalidOperationException>(
                "ServiceProvider.Dispose() refuses to dispose IAsyncDisposable-only services synchronously");
    }

    [Test]
    public async Task DisposeAsync_OnContainerWithAsyncOnlyDisposableSingleton_DisposesService()
    {
        var services = new ServiceCollection();
        services.AddSingleton<AsyncOnlyDisposable>();
        var provider = services.BuildServiceProvider();

        var instance = provider.GetRequiredService<AsyncOnlyDisposable>();

        await provider.DisposeAsync();

        instance.DisposeAsyncCallCount.Should().Be(1);
    }

    [Test]
    public async Task DisposeAsync_OnContainerWithMixedDisposables_DisposesAllInReverseOrder()
    {
        var disposalOrder = new List<string>();
        var services = new ServiceCollection();
        services.AddSingleton(disposalOrder);
        // Use factory registrations so the container owns the lifetime and
        // therefore disposes the instances. Pre-created instances supplied via
        // AddSingleton(instance) are NOT disposed by the container.
        services.AddSingleton<First>(sp => new First(sp.GetRequiredService<List<string>>()));
        services.AddSingleton<Second>(sp => new Second(sp.GetRequiredService<List<string>>()));
        services.AddSingleton<Third>(sp => new Third(sp.GetRequiredService<List<string>>()));

        var provider = services.BuildServiceProvider();
        _ = provider.GetRequiredService<First>();
        _ = provider.GetRequiredService<Second>();
        _ = provider.GetRequiredService<Third>();

        await provider.DisposeAsync();

        disposalOrder.Should()
            .HaveCount(3, "all three singletons are tracked")
            .And.ContainInOrder("third", "second", "first");
    }

    private sealed class AsyncOnlyDisposable : IAsyncDisposable
    {
        public int DisposeAsyncCallCount { get; private set; }

        public ValueTask DisposeAsync()
        {
            DisposeAsyncCallCount++;
            return ValueTask.CompletedTask;
        }
    }

    private class TrackingSyncDisposable : IDisposable
    {
        private readonly string _name;
        private readonly List<string> _log;

        public TrackingSyncDisposable(string name, List<string> log)
        {
            _name = name;
            _log = log;
        }

        public void Dispose() => _log.Add(_name);
    }

    private class TrackingAsyncDisposable : IAsyncDisposable
    {
        private readonly string _name;
        private readonly List<string> _log;

        public TrackingAsyncDisposable(string name, List<string> log)
        {
            _name = name;
            _log = log;
        }

        public ValueTask DisposeAsync()
        {
            _log.Add(_name);
            return ValueTask.CompletedTask;
        }
    }

    private sealed class First : TrackingSyncDisposable
    {
        public First(List<string> log) : base("first", log) { }
    }

    private sealed class Second : TrackingAsyncDisposable
    {
        public Second(List<string> log) : base("second", log) { }
    }

    private sealed class Third : TrackingSyncDisposable
    {
        public Third(List<string> log) : base("third", log) { }
    }
}
