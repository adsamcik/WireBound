using WireBound.Platform.Abstract.Services;

namespace WireBound.Platform.Stub.Services;

/// <summary>
/// Stub implementation that never detects any games.
/// Used as fallback on platforms without game detection support.
/// </summary>
public sealed class StubGameDetectionProvider : IGameDetectionProvider
{
    public bool IsKnownGame(string executablePath) => false;

    public Task InitializeAsync(CancellationToken cancellationToken = default) => Task.CompletedTask;
}
