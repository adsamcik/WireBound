using WireBound.Platform.Abstract.Services;

namespace WireBound.Platform.Stub.Services;

public sealed class StubTrayIconSizeProvider : ITrayIconSizeProvider
{
    public int? GetPixelSize() => null;
}
