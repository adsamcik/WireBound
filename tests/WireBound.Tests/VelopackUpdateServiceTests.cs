using AwesomeAssertions;
using TUnit.Core;
using WireBound.Avalonia.Services;

namespace WireBound.Tests.Services;

/// <summary>
/// VelopackUpdateService creates its own UpdateManager internally and doesn't support DI.
/// Unit testing requires refactoring to accept IUpdateManager abstraction.
/// </summary>
public class VelopackUpdateServiceTests
{
    [Test]
    public void Class_ImplementsIUpdateService()
    {
        typeof(VelopackUpdateService).Should().Implement<WireBound.Core.Services.IUpdateService>();
    }
}
