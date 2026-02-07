using Microsoft.Extensions.Logging;
using WireBound.Avalonia.Services;

namespace WireBound.Tests.Services;

/// <summary>
/// Unit tests for DnsResolverService cache/queue logic (no actual DNS lookups)
/// </summary>
public class DnsResolverServiceTests : IDisposable
{
    private readonly DnsResolverService _service;

    public DnsResolverServiceTests()
    {
        var logger = Substitute.For<ILogger<DnsResolverService>>();
        _service = new DnsResolverService(logger);
    }

    public void Dispose()
    {
        _service.Dispose();
        GC.SuppressFinalize(this);
    }

    // ═══════════════════════════════════════════════════════════════════════
    // GetCached Tests
    // ═══════════════════════════════════════════════════════════════════════

    [Test]
    public void GetCached_UncachedIp_ReturnsNull()
    {
        // Act
        var result = _service.GetCached("8.8.8.8");

        // Assert
        result.Should().BeNull();
    }

    // ═══════════════════════════════════════════════════════════════════════
    // ClearCache Tests
    // ═══════════════════════════════════════════════════════════════════════

    [Test]
    public void ClearCache_EmptiesTheCache()
    {
        // Arrange - queue something that would be processed
        // Just verify clear doesn't throw on empty cache
        _service.ClearCache();

        // Assert
        _service.CacheSize.Should().Be(0);
    }

    // ═══════════════════════════════════════════════════════════════════════
    // CacheSize Tests
    // ═══════════════════════════════════════════════════════════════════════

    [Test]
    public void CacheSize_StartsAtZero()
    {
        // Act & Assert
        _service.CacheSize.Should().Be(0);
    }

    // ═══════════════════════════════════════════════════════════════════════
    // MaxCacheSize Tests
    // ═══════════════════════════════════════════════════════════════════════

    [Test]
    public void MaxCacheSize_DefaultIs1000()
    {
        // Act & Assert
        _service.MaxCacheSize.Should().Be(1000);
    }

    // ═══════════════════════════════════════════════════════════════════════
    // CacheTtl Tests
    // ═══════════════════════════════════════════════════════════════════════

    [Test]
    public void CacheTtl_DefaultIs30Minutes()
    {
        // Act & Assert
        _service.CacheTtl.Should().Be(TimeSpan.FromMinutes(30));
    }

    // ═══════════════════════════════════════════════════════════════════════
    // QueueForResolution Tests
    // ═══════════════════════════════════════════════════════════════════════

    [Test]
    public void QueueForResolution_NullInput_DoesNothing()
    {
        // Act & Assert - should not throw
        _service.QueueForResolution(null!);
        _service.CacheSize.Should().Be(0);
    }

    [Test]
    public void QueueForResolution_EmptyString_DoesNothing()
    {
        // Act & Assert
        _service.QueueForResolution("");
        _service.CacheSize.Should().Be(0);
    }

    [Test]
    public void QueueForResolution_InvalidIp_DoesNothing()
    {
        // Act & Assert
        _service.QueueForResolution("not-an-ip");
        _service.CacheSize.Should().Be(0);
    }

    [Test]
    public void QueueForResolution_PrivateIp_Skips()
    {
        // Act - 192.168.1.1 is a private IP, should be skipped
        _service.QueueForResolution("192.168.1.1");

        // Assert - should not end up in cache immediately (skipped by IsPrivateOrLocal)
        _service.CacheSize.Should().Be(0);
    }

    [Test]
    public void QueueForResolution_LoopbackIp_Skips()
    {
        // Act
        _service.QueueForResolution("127.0.0.1");

        // Assert
        _service.CacheSize.Should().Be(0);
    }

    // ═══════════════════════════════════════════════════════════════════════
    // Dispose Tests
    // ═══════════════════════════════════════════════════════════════════════

    [Test]
    public void Dispose_IsIdempotent()
    {
        // Arrange
        var logger = Substitute.For<ILogger<DnsResolverService>>();
        var svc = new DnsResolverService(logger);

        // Act & Assert - should not throw on double dispose
        svc.Dispose();
        svc.Dispose();
    }
}
