namespace WireBound.Platform.Stub.Services;

/// <summary>
/// Stub implementation of IAppMetadataProvider returning safe defaults.
/// Used as fallback on unsupported platforms.
/// </summary>
public sealed class StubAppMetadataProvider : WireBound.Platform.Abstract.Services.IAppMetadataProvider
{
    public string? GetPublisher(string executablePath) => null;

    public string? GetCategoryFromOsMetadata(string executableName) => null;

    public string? GetParentProcessName(int processId) => null;

    public Task InitializeAsync(CancellationToken cancellationToken = default)
        => Task.CompletedTask;
}
