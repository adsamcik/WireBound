using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using TUnit.Core;
using WireBound.Services;
using WireBound.Models;

namespace WireBound.Tests.Services;

public class GitHubUpdateServiceTests
{
    private const string TestOwner = "test-owner";
    private const string TestRepo = "test-repo";
    
    [Test]
    public async Task CheckForUpdateAsync_WhenNoUpdateAvailable_ReturnsNull()
    {
        // Arrange
        var currentVersion = new Version("2.0.0");
        var latestVersion = "1.5.0";
        
        var httpClient = CreateMockHttpClient(latestVersion, "https://github.com/test/release");
        var service = new GitHubUpdateService(httpClient, TestOwner, TestRepo);
        
        // Act
        var result = await service.CheckForUpdateAsync(currentVersion);
        
        // Assert
        result.Should().BeNull();
    }
    
    [Test]
    public async Task CheckForUpdateAsync_WhenCurrentVersionIsLatest_ReturnsNull()
    {
        // Arrange
        var currentVersion = new Version("2.0.0");
        var latestVersion = "2.0.0";
        
        var httpClient = CreateMockHttpClient(latestVersion, "https://github.com/test/release");
        var service = new GitHubUpdateService(httpClient, TestOwner, TestRepo);
        
        // Act
        var result = await service.CheckForUpdateAsync(currentVersion);
        
        // Assert
        result.Should().BeNull();
    }
    
    [Test]
    public async Task CheckForUpdateAsync_WhenUpdateAvailable_ReturnsUpdateCheckResult()
    {
        // Arrange
        var currentVersion = new Version("1.0.0");
        var latestVersion = "2.0.0";
        var releaseUrl = "https://github.com/test/release/v2.0.0";
        
        var httpClient = CreateMockHttpClient(latestVersion, releaseUrl);
        var service = new GitHubUpdateService(httpClient, TestOwner, TestRepo);
        
        // Act
        var result = await service.CheckForUpdateAsync(currentVersion);
        
        // Assert
        result.Should().NotBeNull();
        result!.LatestVersion.Should().Be(latestVersion);
        result.ReleaseUrl.Should().Be(releaseUrl);
    }
    
    [Test]
    public async Task CheckForUpdateAsync_WhenMajorVersionDifferent_ReturnsUpdate()
    {
        // Arrange
        var currentVersion = new Version("1.9.9");
        var latestVersion = "2.0.0";
        
        var httpClient = CreateMockHttpClient(latestVersion, "https://github.com/test/release");
        var service = new GitHubUpdateService(httpClient, TestOwner, TestRepo);
        
        // Act
        var result = await service.CheckForUpdateAsync(currentVersion);
        
        // Assert
        result.Should().NotBeNull();
        result!.LatestVersion.Should().Be(latestVersion);
    }
    
    [Test]
    public async Task CheckForUpdateAsync_WhenMinorVersionDifferent_ReturnsUpdate()
    {
        // Arrange
        var currentVersion = new Version("2.0.5");
        var latestVersion = "2.1.0";
        
        var httpClient = CreateMockHttpClient(latestVersion, "https://github.com/test/release");
        var service = new GitHubUpdateService(httpClient, TestOwner, TestRepo);
        
        // Act
        var result = await service.CheckForUpdateAsync(currentVersion);
        
        // Assert
        result.Should().NotBeNull();
        result!.LatestVersion.Should().Be(latestVersion);
    }
    
    [Test]
    public async Task CheckForUpdateAsync_WhenPatchVersionDifferent_ReturnsUpdate()
    {
        // Arrange
        var currentVersion = new Version("2.1.0");
        var latestVersion = "2.1.1";
        
        var httpClient = CreateMockHttpClient(latestVersion, "https://github.com/test/release");
        var service = new GitHubUpdateService(httpClient, TestOwner, TestRepo);
        
        // Act
        var result = await service.CheckForUpdateAsync(currentVersion);
        
        // Assert
        result.Should().NotBeNull();
        result!.LatestVersion.Should().Be(latestVersion);
    }
    
    [Test]
    public async Task CheckForUpdateAsync_WithInvalidVersionString_ReturnsNull()
    {
        // Arrange
        var currentVersion = new Version("1.0.0");
        var invalidVersion = "not-a-version";
        
        var httpClient = CreateMockHttpClient(invalidVersion, "https://github.com/test/release");
        var service = new GitHubUpdateService(httpClient, TestOwner, TestRepo);
        
        // Act
        var result = await service.CheckForUpdateAsync(currentVersion);
        
        // Assert
        result.Should().BeNull();
    }
    
    [Test]
    public async Task CheckForUpdateAsync_WithVersionPrefix_ParsesCorrectly()
    {
        // Arrange
        var currentVersion = new Version("1.0.0");
        var latestVersion = "v2.0.0"; // Version with 'v' prefix
        
        var httpClient = CreateMockHttpClient(latestVersion, "https://github.com/test/release");
        var service = new GitHubUpdateService(httpClient, TestOwner, TestRepo);
        
        // Act
        var result = await service.CheckForUpdateAsync(currentVersion);
        
        // Assert
        result.Should().NotBeNull();
        result!.LatestVersion.Should().Be("2.0.0"); // Should strip 'v' prefix
    }
    
    [Test]
    public async Task CheckForUpdateAsync_WhenHttpRequestTimesOut_ReturnsNull()
    {
        // Arrange
        var currentVersion = new Version("1.0.0");
        var httpClient = CreateTimeoutHttpClient();
        var service = new GitHubUpdateService(httpClient, TestOwner, TestRepo);
        
        // Act
        var result = await service.CheckForUpdateAsync(currentVersion);
        
        // Assert
        result.Should().BeNull();
    }
    
    [Test]
    public async Task CheckForUpdateAsync_WhenHttp404_ReturnsNull()
    {
        // Arrange
        var currentVersion = new Version("1.0.0");
        var httpClient = CreateHttpClientWithStatusCode(HttpStatusCode.NotFound);
        var service = new GitHubUpdateService(httpClient, TestOwner, TestRepo);
        
        // Act
        var result = await service.CheckForUpdateAsync(currentVersion);
        
        // Assert
        result.Should().BeNull();
    }
    
    [Test]
    public async Task CheckForUpdateAsync_WhenHttp500_ReturnsNull()
    {
        // Arrange
        var currentVersion = new Version("1.0.0");
        var httpClient = CreateHttpClientWithStatusCode(HttpStatusCode.InternalServerError);
        var service = new GitHubUpdateService(httpClient, TestOwner, TestRepo);
        
        // Act
        var result = await service.CheckForUpdateAsync(currentVersion);
        
        // Assert
        result.Should().BeNull();
    }
    
    [Test]
    public async Task CheckForUpdateAsync_WhenNetworkError_ReturnsNull()
    {
        // Arrange
        var currentVersion = new Version("1.0.0");
        var httpClient = CreateNetworkErrorHttpClient();
        var service = new GitHubUpdateService(httpClient, TestOwner, TestRepo);
        
        // Act
        var result = await service.CheckForUpdateAsync(currentVersion);
        
        // Assert
        result.Should().BeNull();
    }
    
    [Test]
    public async Task CheckForUpdateAsync_ParsesGitHubReleaseJsonCorrectly()
    {
        // Arrange
        var currentVersion = new Version("1.0.0");
        var expectedVersion = "2.5.3";
        var expectedUrl = "https://github.com/owner/repo/releases/tag/v2.5.3";
        var expectedName = "Version 2.5.3 Release";
        var expectedBody = "This is the release notes";
        var expectedPublishedAt = DateTime.Parse("2024-01-15T10:30:00Z").ToUniversalTime();
        
        var releaseJson = new
        {
            tag_name = $"v{expectedVersion}",
            name = expectedName,
            html_url = expectedUrl,
            body = expectedBody,
            published_at = "2024-01-15T10:30:00Z",
            prerelease = false,
            draft = false
        };
        
        var httpClient = CreateMockHttpClientWithJson(releaseJson);
        var service = new GitHubUpdateService(httpClient, TestOwner, TestRepo);
        
        // Act
        var result = await service.CheckForUpdateAsync(currentVersion);
        
        // Assert
        result.Should().NotBeNull();
        result!.LatestVersion.Should().Be(expectedVersion);
        result.ReleaseUrl.Should().Be(expectedUrl);
        result.ReleaseName.Should().Be(expectedName);
        result.ReleaseNotes.Should().Be(expectedBody);
        result.PublishedAt.Should().Be(expectedPublishedAt);
    }
    
    [Test]
    public async Task CheckForUpdateAsync_IgnoresPrereleases()
    {
        // Arrange
        var currentVersion = new Version("1.0.0");
        
        var prereleaseJson = new
        {
            tag_name = "v2.0.0-beta",
            name = "Beta Release",
            html_url = "https://github.com/test/release",
            body = "Beta version",
            published_at = "2024-01-15T10:30:00Z",
            prerelease = true,
            draft = false
        };
        
        var httpClient = CreateMockHttpClientWithJson(prereleaseJson);
        var service = new GitHubUpdateService(httpClient, TestOwner, TestRepo);
        
        // Act
        var result = await service.CheckForUpdateAsync(currentVersion);
        
        // Assert
        result.Should().BeNull();
    }
    
    [Test]
    public async Task CheckForUpdateAsync_IgnoresDrafts()
    {
        // Arrange
        var currentVersion = new Version("1.0.0");
        
        var draftJson = new
        {
            tag_name = "v2.0.0",
            name = "Draft Release",
            html_url = "https://github.com/test/release",
            body = "Draft version",
            published_at = "2024-01-15T10:30:00Z",
            prerelease = false,
            draft = true
        };
        
        var httpClient = CreateMockHttpClientWithJson(draftJson);
        var service = new GitHubUpdateService(httpClient, TestOwner, TestRepo);
        
        // Act
        var result = await service.CheckForUpdateAsync(currentVersion);
        
        // Assert
        result.Should().BeNull();
    }
    
    [Test]
    public async Task CheckForUpdateAsync_WithMalformedJson_ReturnsNull()
    {
        // Arrange
        var currentVersion = new Version("1.0.0");
        var httpClient = CreateMockHttpClientWithInvalidJson();
        var service = new GitHubUpdateService(httpClient, TestOwner, TestRepo);
        
        // Act
        var result = await service.CheckForUpdateAsync(currentVersion);
        
        // Assert
        result.Should().BeNull();
    }
    
    [Test]
    public async Task CheckForUpdateAsync_WithEmptyResponse_ReturnsNull()
    {
        // Arrange
        var currentVersion = new Version("1.0.0");
        var httpClient = CreateMockHttpClientWithEmptyResponse();
        var service = new GitHubUpdateService(httpClient, TestOwner, TestRepo);
        
        // Act
        var result = await service.CheckForUpdateAsync(currentVersion);
        
        // Assert
        result.Should().BeNull();
    }
    
    [Test]
    public async Task CheckForUpdateAsync_WithMissingTagName_ReturnsNull()
    {
        // Arrange
        var currentVersion = new Version("1.0.0");
        
        var incompleteJson = new
        {
            name = "Release without tag",
            html_url = "https://github.com/test/release",
            body = "Missing tag_name field"
        };
        
        var httpClient = CreateMockHttpClientWithJson(incompleteJson);
        var service = new GitHubUpdateService(httpClient, TestOwner, TestRepo);
        
        // Act
        var result = await service.CheckForUpdateAsync(currentVersion);
        
        // Assert
        result.Should().BeNull();
    }
    
    [Test]
    public async Task CheckForUpdateAsync_UsesCorrectApiEndpoint()
    {
        // Arrange
        var currentVersion = new Version("1.0.0");
        string? capturedUrl = null;
        
        var handler = new TestHttpMessageHandler((request) =>
        {
            capturedUrl = request.RequestUri?.ToString();
            var releaseJson = new
            {
                tag_name = "v2.0.0",
                html_url = "https://github.com/test/release",
                published_at = "2024-01-15T10:30:00Z"
            };
            return new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = JsonContent.Create(releaseJson)
            };
        });
        
        var httpClient = new HttpClient(handler);
        var service = new GitHubUpdateService(httpClient, TestOwner, TestRepo);
        
        // Act
        await service.CheckForUpdateAsync(currentVersion);
        
        // Assert
        capturedUrl.Should().Be($"https://api.github.com/repos/{TestOwner}/{TestRepo}/releases/latest");
    }
    
    [Test]
    public async Task CheckForUpdateAsync_SetsUserAgentHeader()
    {
        // Arrange
        var currentVersion = new Version("1.0.0");
        string? capturedUserAgent = null;
        
        var handler = new TestHttpMessageHandler((request) =>
        {
            capturedUserAgent = request.Headers.UserAgent.ToString();
            var releaseJson = new
            {
                tag_name = "v2.0.0",
                html_url = "https://github.com/test/release",
                published_at = "2024-01-15T10:30:00Z"
            };
            return new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = JsonContent.Create(releaseJson)
            };
        });
        
        var httpClient = new HttpClient(handler);
        var service = new GitHubUpdateService(httpClient, TestOwner, TestRepo);
        
        // Act
        await service.CheckForUpdateAsync(currentVersion);
        
        // Assert
        capturedUserAgent.Should().NotBeNullOrEmpty();
        capturedUserAgent.Should().Contain("WireBound");
    }
    
    // Helper methods for creating mock HttpClients
    
    private static HttpClient CreateMockHttpClient(string version, string releaseUrl)
    {
        var releaseJson = new
        {
            tag_name = version.StartsWith("v") ? version : $"v{version}",
            html_url = releaseUrl,
            name = $"Release {version}",
            body = "Release notes",
            published_at = DateTime.UtcNow.ToString("o"),
            prerelease = false,
            draft = false
        };
        
        return CreateMockHttpClientWithJson(releaseJson);
    }
    
    private static HttpClient CreateMockHttpClientWithJson(object jsonObject)
    {
        var handler = new TestHttpMessageHandler((request) =>
        {
            return new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = JsonContent.Create(jsonObject)
            };
        });
        
        return new HttpClient(handler);
    }
    
    private static HttpClient CreateMockHttpClientWithInvalidJson()
    {
        var handler = new TestHttpMessageHandler((request) =>
        {
            return new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent("{invalid json content")
            };
        });
        
        return new HttpClient(handler);
    }
    
    private static HttpClient CreateMockHttpClientWithEmptyResponse()
    {
        var handler = new TestHttpMessageHandler((request) =>
        {
            return new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent("")
            };
        });
        
        return new HttpClient(handler);
    }
    
    private static HttpClient CreateHttpClientWithStatusCode(HttpStatusCode statusCode)
    {
        var handler = new TestHttpMessageHandler((request) =>
        {
            return new HttpResponseMessage(statusCode);
        });
        
        return new HttpClient(handler);
    }
    
    private static HttpClient CreateTimeoutHttpClient()
    {
        var handler = new TestHttpMessageHandler((request) =>
        {
            throw new TaskCanceledException("Request timed out");
        });
        
        return new HttpClient(handler);
    }
    
    private static HttpClient CreateNetworkErrorHttpClient()
    {
        var handler = new TestHttpMessageHandler((request) =>
        {
            throw new HttpRequestException("Network error");
        });
        
        return new HttpClient(handler);
    }
    
    private class TestHttpMessageHandler : HttpMessageHandler
    {
        private readonly Func<HttpRequestMessage, HttpResponseMessage> _handler;
        
        public TestHttpMessageHandler(Func<HttpRequestMessage, HttpResponseMessage> handler)
        {
            _handler = handler;
        }
        
        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request, 
            CancellationToken cancellationToken)
        {
            try
            {
                return Task.FromResult(_handler(request));
            }
            catch (Exception ex)
            {
                return Task.FromException<HttpResponseMessage>(ex);
            }
        }
    }
}
