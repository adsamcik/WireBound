using AwesomeAssertions;
using TUnit.Core;
using WireBound.Avalonia.Services;

namespace WireBound.Tests.Services;

/// <summary>
/// GitHubUpdateService makes real HTTP calls and doesn't support dependency injection.
/// Unit testing requires refactoring to accept HttpClient/IHttpClientFactory.
/// </summary>
public class GitHubUpdateServiceTests
{
    [Test]
    public void Class_ImplementsIUpdateService()
    {
        typeof(GitHubUpdateService).Should().Implement<WireBound.Core.Services.IUpdateService>();
    }
}
