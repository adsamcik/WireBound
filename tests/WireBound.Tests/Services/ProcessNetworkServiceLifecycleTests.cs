using WireBound.Avalonia.Services;
using WireBound.Platform.Abstract.Models;
using WireBound.Platform.Abstract.Services;

namespace WireBound.Tests.Services;

public class ProcessNetworkServiceLifecycleTests
{
    [Test]
    public async Task HandleProviderChangedAsync_TwoConcurrentChanges_SerializesCorrectly()
    {
        var initialProvider = CreateProvider(isMonitoring: true);
        var firstReplacement = CreateProvider(isMonitoring: true);
        var secondReplacement = CreateProvider();
        var firstStopStarted = CreateTcs();
        var releaseFirstStop = CreateTcs();

        initialProvider.StopMonitoringAsync().Returns(_ =>
        {
            firstStopStarted.TrySetResult();
            return releaseFirstStop.Task;
        });

        var factory = new TestProviderFactory(initialProvider);
        using var service = new ProcessNetworkService(factory);
        await service.StartAsync();

        factory.RaiseProviderChanged(firstReplacement);
        var firstChange = service.PendingProviderChangeTask;
        await firstStopStarted.Task.WaitAsync(TimeSpan.FromSeconds(2));

        factory.RaiseProviderChanged(secondReplacement);
        var secondChange = service.PendingProviderChangeTask;
        secondChange.Should().NotBeNull();
        secondChange!.IsCompleted.Should().BeFalse();

        releaseFirstStop.SetResult();
        await Task.WhenAll(firstChange!, secondChange).WaitAsync(TimeSpan.FromSeconds(3));

        await initialProvider.Received(1).StopMonitoringAsync();
        await firstReplacement.Received(1).StartMonitoringAsync(Arg.Any<CancellationToken>());
        await firstReplacement.Received(1).StopMonitoringAsync();
        await secondReplacement.Received(1).StartMonitoringAsync(Arg.Any<CancellationToken>());

        var expectedStats = new List<ConnectionStats> { new() };
        secondReplacement.GetConnectionStatsAsync(Arg.Any<CancellationToken>())
            .Returns(Task.FromResult<IReadOnlyList<ConnectionStats>>(expectedStats));

        var actualStats = await service.GetConnectionStatsAsync();

        actualStats.Should().BeSameAs(expectedStats);
    }

    [Test]
    public async Task StopAsync_WhileProviderChangeInProgress_DoesNotRace()
    {
        var initialProvider = CreateProvider(isMonitoring: true);
        var replacementProvider = CreateProvider();
        var stopStarted = CreateTcs();
        var releaseStop = CreateTcs();

        initialProvider.StopMonitoringAsync().Returns(_ =>
        {
            stopStarted.TrySetResult();
            return releaseStop.Task;
        });

        var factory = new TestProviderFactory(initialProvider);
        using var service = new ProcessNetworkService(factory);
        await service.StartAsync();

        factory.RaiseProviderChanged(replacementProvider);
        var providerChange = service.PendingProviderChangeTask;
        await stopStarted.Task.WaitAsync(TimeSpan.FromSeconds(2));

        var stopTask = service.StopAsync();
        await Task.Delay(100);
        stopTask.IsCompleted.Should().BeFalse();

        releaseStop.SetResult();
        await Task.WhenAll(providerChange!, stopTask).WaitAsync(TimeSpan.FromSeconds(3));

        await initialProvider.Received(1).StopMonitoringAsync();
        await replacementProvider.Received(1).StartMonitoringAsync(Arg.Any<CancellationToken>());
        await replacementProvider.Received(1).StopMonitoringAsync();
    }

    [Test]
    public async Task Dispose_AwaitsPendingProviderChange()
    {
        var initialProvider = CreateProvider(isMonitoring: true);
        var replacementProvider = CreateProvider();
        var stopStarted = CreateTcs();
        var releaseStop = CreateTcs();

        initialProvider.StopMonitoringAsync().Returns(_ =>
        {
            stopStarted.TrySetResult();
            return releaseStop.Task;
        });

        var factory = new TestProviderFactory(initialProvider);
        var service = new ProcessNetworkService(factory);
        await service.StartAsync();

        factory.RaiseProviderChanged(replacementProvider);
        await stopStarted.Task.WaitAsync(TimeSpan.FromSeconds(2));

        var disposeTask = Task.Run(service.Dispose);
        await Task.Delay(100);
        disposeTask.IsCompleted.Should().BeFalse();

        releaseStop.SetResult();
        await disposeTask.WaitAsync(TimeSpan.FromSeconds(3));

        await initialProvider.Received(1).StopMonitoringAsync();
        replacementProvider.Received(1).Dispose();
    }

    [Test]
    public async Task IsRunning_ReadsCurrentProviderAtomically()
    {
        var factory = new TestProviderFactory(CreateProvider(isMonitoring: true));
        using var service = new ProcessNetworkService(factory);
        await service.StartAsync();

        var act = async () =>
        {
            using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(2));
            var readTask = Task.Run(() =>
            {
                while (!cts.IsCancellationRequested)
                {
                    _ = service.IsRunning;
                }
            });

            for (var i = 0; i < 50; i++)
            {
                factory.RaiseProviderChanged(CreateProvider(isMonitoring: i % 2 == 0));
                var pendingChange = service.PendingProviderChangeTask;
                if (pendingChange is not null)
                {
                    await pendingChange.WaitAsync(TimeSpan.FromSeconds(2));
                }
            }

            cts.Cancel();
            await readTask.WaitAsync(TimeSpan.FromSeconds(2));
        };

        await act.Should().NotThrowAsync();
    }

    private static IProcessNetworkProvider CreateProvider(bool isMonitoring = false)
    {
        var provider = Substitute.For<IProcessNetworkProvider>();
        var monitoring = isMonitoring;

        provider.Capabilities.Returns(ProcessNetworkCapabilities.ConnectionList);
        provider.IsMonitoring.Returns(_ => monitoring);
        provider.StartMonitoringAsync(Arg.Any<CancellationToken>()).Returns(_ =>
        {
            monitoring = true;
            return Task.FromResult(true);
        });
        provider.StopMonitoringAsync().Returns(_ =>
        {
            monitoring = false;
            return Task.CompletedTask;
        });
        provider.GetConnectionStatsAsync(Arg.Any<CancellationToken>())
            .Returns(Task.FromResult<IReadOnlyList<ConnectionStats>>(Array.Empty<ConnectionStats>()));

        return provider;
    }

    private static TaskCompletionSource CreateTcs()
        => new(TaskCreationOptions.RunContinuationsAsynchronously);

    private sealed class TestProviderFactory(IProcessNetworkProvider provider) : IProcessNetworkProviderFactory
    {
        private IProcessNetworkProvider _provider = provider;

        public bool HasElevatedProvider { get; set; }

        public event EventHandler<ProviderChangedEventArgs>? ProviderChanged;

        public IProcessNetworkProvider GetProvider() => _provider;

        public void RaiseProviderChanged(IProcessNetworkProvider provider)
        {
            _provider = provider;
            ProviderChanged?.Invoke(this, new ProviderChangedEventArgs(provider));
        }
    }
}
