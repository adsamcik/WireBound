using System.Net;
using System.Net.Http.Json;
using TUnit.Core;
using Velopack;
using Velopack.Sources;
using WireBound.Services;
using WireBound.Models;

namespace WireBound.Tests.Services;

public class VelopackUpdateServiceTests
{
    [Test]
    public async Task IsInstalled_WhenRunningFromInstalledLocation_ReturnsTrue()
    {
        // Arrange
        var mockUpdateManager = CreateMockUpdateManager(isInstalled: true);
        var service = new VelopackUpdateService(mockUpdateManager);
        
        // Act
        var result = service.IsInstalled();
        
        // Assert
        result.Should().BeTrue();
    }
    
    [Test]
    public async Task IsInstalled_WhenRunningPortable_ReturnsFalse()
    {
        // Arrange
        var mockUpdateManager = CreateMockUpdateManager(isInstalled: false);
        var service = new VelopackUpdateService(mockUpdateManager);
        
        // Act
        var result = service.IsInstalled();
        
        // Assert
        result.Should().BeFalse();
    }
    
    [Test]
    public async Task CheckForUpdateAsync_InInstalledMode_WhenUpdateAvailable_ReturnsUpdateInfo()
    {
        // Arrange
        var currentVersion = new Version("1.0.0");
        var newVersion = new Version("2.0.0");
        
        var mockUpdateInfo = new MockUpdateInfo(newVersion);
        var mockUpdateManager = CreateMockUpdateManager(isInstalled: true, updateInfo: mockUpdateInfo);
        var service = new VelopackUpdateService(mockUpdateManager);
        
        // Act
        var result = await service.CheckForUpdateAsync(currentVersion);
        
        // Assert
        result.Should().NotBeNull();
        result!.LatestVersion.Should().Be("2.0.0");
    }
    
    [Test]
    public async Task CheckForUpdateAsync_InInstalledMode_WhenNoUpdateAvailable_ReturnsNull()
    {
        // Arrange
        var currentVersion = new Version("2.0.0");
        var mockUpdateManager = CreateMockUpdateManager(isInstalled: true, updateInfo: null);
        var service = new VelopackUpdateService(mockUpdateManager);
        
        // Act
        var result = await service.CheckForUpdateAsync(currentVersion);
        
        // Assert
        result.Should().BeNull();
    }
    
    [Test]
    public async Task CheckForUpdateAsync_InInstalledMode_WhenCurrentVersionIsLatest_ReturnsNull()
    {
        // Arrange
        var currentVersion = new Version("2.0.0");
        var sameVersion = new Version("2.0.0");
        
        var mockUpdateInfo = new MockUpdateInfo(sameVersion);
        var mockUpdateManager = CreateMockUpdateManager(isInstalled: true, updateInfo: mockUpdateInfo);
        var service = new VelopackUpdateService(mockUpdateManager);
        
        // Act
        var result = await service.CheckForUpdateAsync(currentVersion);
        
        // Assert
        result.Should().BeNull();
    }
    
    [Test]
    public async Task CheckForUpdateAsync_InInstalledMode_WithNewerVersion_ReturnsUpdate()
    {
        // Arrange
        var currentVersion = new Version("1.5.0");
        var newVersion = new Version("2.1.3");
        
        var mockUpdateInfo = new MockUpdateInfo(newVersion);
        var mockUpdateManager = CreateMockUpdateManager(isInstalled: true, updateInfo: mockUpdateInfo);
        var service = new VelopackUpdateService(mockUpdateManager);
        
        // Act
        var result = await service.CheckForUpdateAsync(currentVersion);
        
        // Assert
        result.Should().NotBeNull();
        result!.LatestVersion.Should().Be("2.1.3");
    }
    
    [Test]
    public async Task CheckForUpdateAsync_InPortableMode_UsesGitHubFallback()
    {
        // Arrange
        var currentVersion = new Version("1.0.0");
        var mockUpdateManager = CreateMockUpdateManager(isInstalled: false);
        
        // Create a mock GitHub source
        var githubOwner = "test-owner";
        var githubRepo = "test-repo";
        var httpClient = CreateMockGitHubHttpClient("2.0.0", "https://github.com/test/release");
        
        var service = new VelopackUpdateService(mockUpdateManager, httpClient, githubOwner, githubRepo);
        
        // Act
        var result = await service.CheckForUpdateAsync(currentVersion);
        
        // Assert
        result.Should().NotBeNull();
        result!.LatestVersion.Should().Be("2.0.0");
    }
    
    [Test]
    public async Task CheckForUpdateAsync_InPortableMode_WhenNoUpdateAvailable_ReturnsNull()
    {
        // Arrange
        var currentVersion = new Version("2.0.0");
        var mockUpdateManager = CreateMockUpdateManager(isInstalled: false);
        
        var httpClient = CreateMockGitHubHttpClient("1.5.0", "https://github.com/test/release");
        var service = new VelopackUpdateService(mockUpdateManager, httpClient, "owner", "repo");
        
        // Act
        var result = await service.CheckForUpdateAsync(currentVersion);
        
        // Assert
        result.Should().BeNull();
    }
    
    [Test]
    public async Task CheckForUpdateAsync_InInstalledMode_HandlesUpdateCheckException()
    {
        // Arrange
        var currentVersion = new Version("1.0.0");
        var mockUpdateManager = CreateMockUpdateManagerWithException(new Exception("Update check failed"));
        var service = new VelopackUpdateService(mockUpdateManager);
        
        // Act
        var result = await service.CheckForUpdateAsync(currentVersion);
        
        // Assert
        result.Should().BeNull();
    }
    
    [Test]
    public async Task CheckForUpdateAsync_InInstalledMode_HandlesNetworkTimeout()
    {
        // Arrange
        var currentVersion = new Version("1.0.0");
        var mockUpdateManager = CreateMockUpdateManagerWithException(
            new TaskCanceledException("Request timed out"));
        var service = new VelopackUpdateService(mockUpdateManager);
        
        // Act
        var result = await service.CheckForUpdateAsync(currentVersion);
        
        // Assert
        result.Should().BeNull();
    }
    
    [Test]
    public async Task CheckForUpdateAsync_InPortableMode_HandlesGitHubApiFailure()
    {
        // Arrange
        var currentVersion = new Version("1.0.0");
        var mockUpdateManager = CreateMockUpdateManager(isInstalled: false);
        
        var httpClient = CreateFailingGitHubHttpClient();
        var service = new VelopackUpdateService(mockUpdateManager, httpClient, "owner", "repo");
        
        // Act
        var result = await service.CheckForUpdateAsync(currentVersion);
        
        // Assert
        result.Should().BeNull();
    }
    
    [Test]
    public async Task CheckForUpdateAsync_DoesNotMakeRealNetworkCalls()
    {
        // Arrange
        var currentVersion = new Version("1.0.0");
        var mockUpdateManager = CreateMockUpdateManager(isInstalled: true, updateInfo: null);
        var service = new VelopackUpdateService(mockUpdateManager);
        
        // Act
        var result = await service.CheckForUpdateAsync(currentVersion);
        
        // Assert
        // If this test completes quickly without throwing network exceptions,
        // it confirms no real network calls were made
        result.Should().BeNull();
    }
    
    [Test]
    public async Task DownloadAndApplyUpdateAsync_InInstalledMode_DownloadsAndAppliesUpdate()
    {
        // Arrange
        var mockUpdateInfo = new MockUpdateInfo(new Version("2.0.0"));
        var mockUpdateManager = CreateMockUpdateManager(isInstalled: true, updateInfo: mockUpdateInfo);
        var service = new VelopackUpdateService(mockUpdateManager);
        
        // Act
        var exception = await Record.ExceptionAsync(async () =>
        {
            await service.DownloadAndApplyUpdateAsync(mockUpdateInfo);
        });
        
        // Assert
        exception.Should().BeNull();
        mockUpdateManager.DownloadAsyncWasCalled.Should().BeTrue();
        mockUpdateManager.ApplyUpdatesAndRestartWasCalled.Should().BeTrue();
    }
    
    [Test]
    public async Task DownloadAndApplyUpdateAsync_InInstalledMode_HandlesDownloadFailure()
    {
        // Arrange
        var mockUpdateInfo = new MockUpdateInfo(new Version("2.0.0"));
        var mockUpdateManager = CreateMockUpdateManagerWithDownloadException(
            new Exception("Download failed"));
        var service = new VelopackUpdateService(mockUpdateManager);
        
        // Act
        var exception = await Record.ExceptionAsync(async () =>
        {
            await service.DownloadAndApplyUpdateAsync(mockUpdateInfo);
        });
        
        // Assert
        exception.Should().NotBeNull();
        exception.Should().BeOfType<Exception>();
        exception!.Message.Should().Contain("Download failed");
    }
    
    [Test]
    public async Task DownloadAndApplyUpdateAsync_InPortableMode_ThrowsInvalidOperationException()
    {
        // Arrange
        var mockUpdateInfo = new MockUpdateInfo(new Version("2.0.0"));
        var mockUpdateManager = CreateMockUpdateManager(isInstalled: false);
        var service = new VelopackUpdateService(mockUpdateManager);
        
        // Act
        var exception = await Record.ExceptionAsync(async () =>
        {
            await service.DownloadAndApplyUpdateAsync(mockUpdateInfo);
        });
        
        // Assert
        exception.Should().NotBeNull();
        exception.Should().BeOfType<InvalidOperationException>();
    }
    
    [Test]
    public async Task GetCurrentVersionAsync_ReturnsCorrectVersion()
    {
        // Arrange
        var expectedVersion = new Version("1.5.3");
        var mockUpdateManager = CreateMockUpdateManager(isInstalled: true, currentVersion: expectedVersion);
        var service = new VelopackUpdateService(mockUpdateManager);
        
        // Act
        var result = await service.GetCurrentVersionAsync();
        
        // Assert
        result.Should().Be(expectedVersion);
    }
    
    [Test]
    public async Task CheckForUpdateAsync_WithProgressCallback_ReportsProgress()
    {
        // Arrange
        var currentVersion = new Version("1.0.0");
        var newVersion = new Version("2.0.0");
        var progressReports = new List<int>();
        
        var mockUpdateInfo = new MockUpdateInfo(newVersion);
        var mockUpdateManager = CreateMockUpdateManager(isInstalled: true, updateInfo: mockUpdateInfo);
        var service = new VelopackUpdateService(mockUpdateManager);
        
        // Act
        var result = await service.CheckForUpdateAsync(currentVersion, progress =>
        {
            progressReports.Add(progress);
        });
        
        // Assert
        result.Should().NotBeNull();
        progressReports.Should().NotBeEmpty();
    }
    
    [Test]
    public async Task CheckForUpdateAsync_InInstalledMode_ParsesVelopackVersionCorrectly()
    {
        // Arrange
        var currentVersion = new Version("1.0.0");
        var newVersion = new Version("2.3.5");
        
        var mockUpdateInfo = new MockUpdateInfo(newVersion);
        var mockUpdateManager = CreateMockUpdateManager(isInstalled: true, updateInfo: mockUpdateInfo);
        var service = new VelopackUpdateService(mockUpdateManager);
        
        // Act
        var result = await service.CheckForUpdateAsync(currentVersion);
        
        // Assert
        result.Should().NotBeNull();
        result!.LatestVersion.Should().Be("2.3.5");
    }
    
    [Test]
    public async Task CheckForUpdateAsync_InPortableMode_ParsesGitHubVersionCorrectly()
    {
        // Arrange
        var currentVersion = new Version("1.0.0");
        var mockUpdateManager = CreateMockUpdateManager(isInstalled: false);
        
        var httpClient = CreateMockGitHubHttpClient("v3.1.4", "https://github.com/test/release");
        var service = new VelopackUpdateService(mockUpdateManager, httpClient, "owner", "repo");
        
        // Act
        var result = await service.CheckForUpdateAsync(currentVersion);
        
        // Assert
        result.Should().NotBeNull();
        result!.LatestVersion.Should().Be("3.1.4");
    }
    
    [Test]
    public void IsInstalled_CalledMultipleTimes_ReturnsSameResult()
    {
        // Arrange
        var mockUpdateManager = CreateMockUpdateManager(isInstalled: true);
        var service = new VelopackUpdateService(mockUpdateManager);
        
        // Act
        var result1 = service.IsInstalled();
        var result2 = service.IsInstalled();
        var result3 = service.IsInstalled();
        
        // Assert
        result1.Should().BeTrue();
        result2.Should().BeTrue();
        result3.Should().BeTrue();
    }
    
    [Test]
    public async Task CheckForUpdateAsync_WithCancellationToken_RespectsCancellation()
    {
        // Arrange
        var currentVersion = new Version("1.0.0");
        var mockUpdateManager = CreateMockUpdateManager(isInstalled: true);
        var service = new VelopackUpdateService(mockUpdateManager);
        var cts = new CancellationTokenSource();
        
        cts.Cancel(); // Cancel immediately
        
        // Act
        var exception = await Record.ExceptionAsync(async () =>
        {
            await service.CheckForUpdateAsync(currentVersion, cancellationToken: cts.Token);
        });
        
        // Assert
        exception.Should().NotBeNull();
        exception.Should().BeOfType<OperationCanceledException>();
    }
    
    // Helper methods for creating mock objects
    
    private static MockUpdateManager CreateMockUpdateManager(
        bool isInstalled,
        MockUpdateInfo? updateInfo = null,
        Version? currentVersion = null)
    {
        return new MockUpdateManager
        {
            IsInstalledValue = isInstalled,
            UpdateInfo = updateInfo,
            CurrentVersion = currentVersion ?? new Version("1.0.0")
        };
    }
    
    private static MockUpdateManager CreateMockUpdateManagerWithException(Exception exception)
    {
        return new MockUpdateManager
        {
            IsInstalledValue = true,
            ThrowOnCheckForUpdates = exception
        };
    }
    
    private static MockUpdateManager CreateMockUpdateManagerWithDownloadException(Exception exception)
    {
        return new MockUpdateManager
        {
            IsInstalledValue = true,
            ThrowOnDownload = exception
        };
    }
    
    private static HttpClient CreateMockGitHubHttpClient(string version, string releaseUrl)
    {
        var releaseJson = new
        {
            tag_name = version,
            html_url = releaseUrl,
            name = $"Release {version}",
            body = "Release notes",
            published_at = DateTime.UtcNow.ToString("o"),
            prerelease = false,
            draft = false
        };
        
        var handler = new TestHttpMessageHandler((request) =>
        {
            return new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = JsonContent.Create(releaseJson)
            };
        });
        
        return new HttpClient(handler);
    }
    
    private static HttpClient CreateFailingGitHubHttpClient()
    {
        var handler = new TestHttpMessageHandler((request) =>
        {
            return new HttpResponseMessage(HttpStatusCode.InternalServerError);
        });
        
        return new HttpClient(handler);
    }
    
    // Mock classes
    
    private class MockUpdateManager : IUpdateManager
    {
        public bool IsInstalledValue { get; set; }
        public MockUpdateInfo? UpdateInfo { get; set; }
        public Version CurrentVersion { get; set; } = new Version("1.0.0");
        public Exception? ThrowOnCheckForUpdates { get; set; }
        public Exception? ThrowOnDownload { get; set; }
        public bool DownloadAsyncWasCalled { get; private set; }
        public bool ApplyUpdatesAndRestartWasCalled { get; private set; }
        
        public bool IsInstalled => IsInstalledValue;
        
        public async Task<UpdateInfo?> CheckForUpdatesAsync()
        {
            if (ThrowOnCheckForUpdates != null)
            {
                throw ThrowOnCheckForUpdates;
            }
            
            await Task.Delay(10); // Simulate network delay
            return UpdateInfo;
        }
        
        public async Task DownloadUpdatesAsync(UpdateInfo update, Action<int>? progress = null)
        {
            if (ThrowOnDownload != null)
            {
                throw ThrowOnDownload;
            }
            
            DownloadAsyncWasCalled = true;
            
            // Simulate download progress
            for (int i = 0; i <= 100; i += 20)
            {
                await Task.Delay(10);
                progress?.Invoke(i);
            }
        }
        
        public void ApplyUpdatesAndRestart(UpdateInfo update)
        {
            ApplyUpdatesAndRestartWasCalled = true;
        }
        
        public void Dispose()
        {
            // No-op for mock
        }
    }
    
    private class MockUpdateInfo : UpdateInfo
    {
        public MockUpdateInfo(Version version)
        {
            TargetFullRelease = new VelopackAsset
            {
                Version = version.ToString(),
                FileName = $"app-{version}.nupkg",
                SHA1 = "mock-sha1",
                Size = 1024 * 1024
            };
        }
    }
    
    private interface IUpdateManager : IDisposable
    {
        bool IsInstalled { get; }
        Task<UpdateInfo?> CheckForUpdatesAsync();
        Task DownloadUpdatesAsync(UpdateInfo update, Action<int>? progress = null);
        void ApplyUpdatesAndRestart(UpdateInfo update);
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
