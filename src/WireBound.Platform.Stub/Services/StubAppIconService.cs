using WireBound.Platform.Abstract.Services;

namespace WireBound.Platform.Stub.Services;

/// <summary>
/// No-op icon resolver used on platforms without an extraction implementation
/// (currently Linux) and as the default before a real provider registers.
/// </summary>
public sealed class StubAppIconService : IAppIconService
{
    public Task<string?> GetIconPathAsync(
        string executablePath,
        string appIdentifier,
        CancellationToken ct = default)
        => Task.FromResult<string?>(null);
}
