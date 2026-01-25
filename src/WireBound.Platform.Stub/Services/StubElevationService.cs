using WireBound.Platform.Abstract.Services;

namespace WireBound.Platform.Stub.Services;

/// <summary>
/// Stub implementation of <see cref="IElevationService"/> for unsupported platforms
/// or development/testing scenarios.
/// </summary>
/// <remarks>
/// This implementation always reports as non-elevated and returns NotSupported
/// for elevation requests. It is used as a fallback when running on platforms
/// without specific elevation support or during testing.
/// </remarks>
public sealed class StubElevationService : IElevationService
{
    /// <inheritdoc />
    /// <remarks>Always returns false in stub implementation.</remarks>
    public bool IsHelperConnected => false;

    /// <inheritdoc />
    /// <remarks>Always returns false in stub implementation.</remarks>
    public bool IsElevated => false;

    /// <inheritdoc />
    /// <remarks>Always returns false since elevation is not supported.</remarks>
    public bool RequiresElevation => false;

    /// <inheritdoc />
    /// <remarks>Always returns false in stub implementation.</remarks>
    public bool IsElevationSupported => false;

    /// <inheritdoc />
#pragma warning disable CS0067 // Event is never used in stub
    public event EventHandler<HelperConnectionStateChangedEventArgs>? HelperConnectionStateChanged;
#pragma warning restore CS0067

    /// <inheritdoc />
    public Task<ElevationResult> StartHelperAsync(CancellationToken cancellationToken = default)
    {
        return Task.FromResult(ElevationResult.NotSupported());
    }

    /// <inheritdoc />
    public Task StopHelperAsync()
    {
        return Task.CompletedTask;
    }

    /// <inheritdoc />
    public IHelperConnection? GetHelperConnection()
    {
        return null;
    }

    /// <inheritdoc />
    /// <remarks>Always returns false since no features require elevation in stub.</remarks>
    public bool RequiresElevationFor(ElevatedFeature feature)
    {
        return false;
    }
}
