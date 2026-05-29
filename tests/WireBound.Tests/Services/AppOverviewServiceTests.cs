using System.Data.Common;
using System.Diagnostics;
using AwesomeAssertions;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.DependencyInjection;
using WireBound.Avalonia.Services;
using WireBound.Core.Data;
using WireBound.Core.Models;
using WireBound.Core.Services;
using WireBound.Platform.Abstract.Helpers;
using WireBound.Tests.Fixtures;

namespace WireBound.Tests.Services;

public class AppOverviewServiceTests : DatabaseTestBase
{
    private readonly AppOverviewService _service;

    public AppOverviewServiceTests()
    {
        _service = new AppOverviewService(ServiceProvider);
    }

    [Test, Timeout(30000)]
    public async Task GetOverviewAsync_WithNetworkAndResourceData_JoinsByAppIdentifier(CancellationToken cancellationToken)
    {
        // Arrange
        var day = DateOnly.FromDateTime(DateTime.Now);
        var hour10 = day.ToDateTime(new TimeOnly(10, 0));
        var hour11 = day.ToDateTime(new TimeOnly(11, 0));
        var hour12 = day.ToDateTime(new TimeOnly(12, 0));

        using (var scope = CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<WireBoundDbContext>();
            db.AppUsageRecords.AddRange(
                CreateNetworkRecord("app-1", hour10, bytesReceived: 100, bytesSent: 40, peakDown: 10, peakUp: 4, appName: "Network Name", processName: "network.exe", executablePath: "C:\\Apps\\network.exe", lastUpdated: hour10),
                CreateNetworkRecord("app-1", hour11, bytesReceived: 200, bytesSent: 60, peakDown: 25, peakUp: 6, appName: "Network Name", processName: "network.exe", executablePath: "C:\\Apps\\network.exe", lastUpdated: hour11));
            db.ResourceInsightSnapshots.AddRange(
                CreateResourceSnapshot("app-1", hour11, cpuPercent: 10, privateBytes: 1_000, peakCpuPercent: 15, peakPrivateBytes: 1_500, appName: "Resource Name", categoryName: "Browser", lastUpdated: hour11),
                CreateResourceSnapshot("app-1", hour12, cpuPercent: 30, privateBytes: 3_000, peakCpuPercent: 40, peakPrivateBytes: 3_500, appName: "Resource Name", categoryName: "Browser", lastUpdated: hour12));
            await db.SaveChangesAsync(cancellationToken);
        }

        // Act
        var result = await _service.GetOverviewAsync(day, day, cancellationToken);

        // Assert
        var overview = result.Should().ContainSingle().Subject;
        overview.AppIdentifier.Should().Be("app-1");
        overview.AppName.Should().Be("Resource Name");
        overview.ProcessName.Should().Be("network.exe");
        overview.ExecutablePath.Should().Be("C:\\Apps\\network.exe");
        overview.CategoryName.Should().Be("Browser");
        overview.BytesReceived.Should().Be(300);
        overview.BytesSent.Should().Be(100);
        overview.PeakDownloadSpeed.Should().Be(25);
        overview.PeakUploadSpeed.Should().Be(6);
        overview.AvgCpuPercent.Should().Be(20);
        overview.MaxCpuPercent.Should().Be(40);
        overview.AvgPrivateBytes.Should().Be(2_000);
        overview.PeakPrivateBytes.Should().Be(3_500);
        overview.FirstSeen.Should().Be(hour10);
        overview.LastSeen.Should().Be(hour12);
        overview.HoursActive.Should().Be(3);
        overview.TotalBytes.Should().Be(400);
        overview.FormattedTotalBytes.Should().Be("400 B");
    }

    [Test, Timeout(30000)]
    public async Task GetOverviewAsync_AppWithOnlyNetworkData_HasZeroCpuAndRam(CancellationToken cancellationToken)
    {
        // Arrange
        var day = DateOnly.FromDateTime(DateTime.Now);
        var hour = day.ToDateTime(new TimeOnly(9, 0));

        using (var scope = CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<WireBoundDbContext>();
            db.AppUsageRecords.Add(CreateNetworkRecord("network-only", hour, bytesReceived: 500, bytesSent: 250, appName: "Network Only", processName: "net.exe", executablePath: "C:\\Apps\\net.exe"));
            await db.SaveChangesAsync(cancellationToken);
        }

        // Act
        var result = await _service.GetOverviewAsync(day, day, cancellationToken);

        // Assert
        var overview = result.Should().ContainSingle().Subject;
        overview.AppIdentifier.Should().Be("network-only");
        overview.AppName.Should().Be("Network Only");
        overview.CategoryName.Should().BeEmpty();
        overview.BytesReceived.Should().Be(500);
        overview.BytesSent.Should().Be(250);
        overview.AvgCpuPercent.Should().Be(0);
        overview.MaxCpuPercent.Should().Be(0);
        overview.AvgPrivateBytes.Should().Be(0);
        overview.PeakPrivateBytes.Should().Be(0);
        overview.FirstSeen.Should().Be(hour);
        overview.LastSeen.Should().Be(hour);
        overview.HoursActive.Should().Be(1);
    }

    [Test, Timeout(30000)]
    public async Task GetOverviewAsync_AppWithOnlyResourceData_HasZeroBytes(CancellationToken cancellationToken)
    {
        // Arrange
        var day = DateOnly.FromDateTime(DateTime.Now);
        var hour = day.ToDateTime(new TimeOnly(8, 0));

        using (var scope = CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<WireBoundDbContext>();
            db.ResourceInsightSnapshots.Add(CreateResourceSnapshot("resource-only", hour, cpuPercent: 12.5, privateBytes: 4_096, peakCpuPercent: 20, peakPrivateBytes: 8_192, appName: "Resource Only", categoryName: "Tool"));
            await db.SaveChangesAsync(cancellationToken);
        }

        // Act
        var result = await _service.GetOverviewAsync(day, day, cancellationToken);

        // Assert
        var overview = result.Should().ContainSingle().Subject;
        overview.AppIdentifier.Should().Be("resource-only");
        overview.AppName.Should().Be("Resource Only");
        overview.ProcessName.Should().BeEmpty();
        overview.ExecutablePath.Should().BeEmpty();
        overview.CategoryName.Should().Be("Tool");
        overview.BytesReceived.Should().Be(0);
        overview.BytesSent.Should().Be(0);
        overview.AvgCpuPercent.Should().Be(12.5);
        overview.MaxCpuPercent.Should().Be(20);
        overview.AvgPrivateBytes.Should().Be(4_096);
        overview.PeakPrivateBytes.Should().Be(8_192);
    }

    [Test, Timeout(30000)]
    public async Task GetOverviewAsync_OutsideRange_NotIncluded(CancellationToken cancellationToken)
    {
        // Arrange
        var today = DateOnly.FromDateTime(DateTime.Now);
        var yesterday = today.AddDays(-1);
        var tomorrow = today.AddDays(1);

        using (var scope = CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<WireBoundDbContext>();
            db.AppUsageRecords.Add(CreateNetworkRecord("outside", yesterday.ToDateTime(new TimeOnly(23, 0)), bytesReceived: 100, bytesSent: 100));
            db.ResourceInsightSnapshots.Add(CreateResourceSnapshot("future", tomorrow.ToDateTime(new TimeOnly(1, 0)), cpuPercent: 10, privateBytes: 100));
            await db.SaveChangesAsync(cancellationToken);
        }

        // Act
        var result = await _service.GetOverviewAsync(today, today, cancellationToken);

        // Assert
        result.Should().BeEmpty();
    }

    [Test, Timeout(30000)]
    public async Task GetOverviewAsync_UnknownIdentifierBucket_IsExcluded(CancellationToken cancellationToken)
    {
        // Arrange
        var day = DateOnly.FromDateTime(DateTime.Now);
        var hour = day.ToDateTime(new TimeOnly(13, 0));

        using (var scope = CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<WireBoundDbContext>();
            db.AppUsageRecords.AddRange(
                CreateNetworkRecord(AppIdentity.UnknownIdentifier, hour, bytesReceived: 100, bytesSent: 100),
                CreateNetworkRecord(string.Empty, hour, bytesReceived: 200, bytesSent: 200),
                CreateNetworkRecord("known", hour, bytesReceived: 300, bytesSent: 300));
            db.ResourceInsightSnapshots.AddRange(
                CreateResourceSnapshot(AppIdentity.UnknownIdentifier, hour, cpuPercent: 10, privateBytes: 100),
                CreateResourceSnapshot(string.Empty, hour, cpuPercent: 20, privateBytes: 200));
            await db.SaveChangesAsync(cancellationToken);
        }

        // Act
        var result = await _service.GetOverviewAsync(day, day, cancellationToken);

        // Assert
        var overview = result.Should().ContainSingle().Subject;
        overview.AppIdentifier.Should().Be("known");
        overview.BytesReceived.Should().Be(300);
        overview.BytesSent.Should().Be(300);
    }

    [Test, Timeout(30000)]
    public async Task GetOverviewAsync_PrefersLatestDisplayName(CancellationToken cancellationToken)
    {
        // Arrange
        var day = DateOnly.FromDateTime(DateTime.Now);
        var hour = day.ToDateTime(new TimeOnly(14, 0));

        using (var scope = CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<WireBoundDbContext>();
            db.AppUsageRecords.Add(CreateNetworkRecord("app-name", hour, bytesReceived: 100, bytesSent: 0, appName: "Old Network Name", processName: "old.exe", executablePath: "C:\\Old\\old.exe", lastUpdated: hour));
            db.ResourceInsightSnapshots.Add(CreateResourceSnapshot("app-name", hour, cpuPercent: 1, privateBytes: 1, appName: "Latest Resource Name", categoryName: "Utility", lastUpdated: hour.AddMinutes(10)));
            await db.SaveChangesAsync(cancellationToken);
        }

        // Act
        var result = await _service.GetOverviewAsync(day, day, cancellationToken);

        // Assert
        var overview = result.Should().ContainSingle().Subject;
        overview.AppName.Should().Be("Latest Resource Name");
        overview.ProcessName.Should().Be("old.exe");
        overview.ExecutablePath.Should().Be("C:\\Old\\old.exe");
    }

    [Test, Timeout(30000)]
    public async Task GetNetworkHistoryAsync_ReturnsOneRowPerHourBucket(CancellationToken cancellationToken)
    {
        // Arrange
        var day = DateOnly.FromDateTime(DateTime.Now);
        var hour1 = day.ToDateTime(new TimeOnly(1, 0));
        var hour2 = day.ToDateTime(new TimeOnly(2, 0));

        using (var scope = CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<WireBoundDbContext>();
            db.AppUsageRecords.AddRange(
                CreateNetworkRecord("history", hour1, bytesReceived: 100, bytesSent: 50),
                CreateNetworkRecord("history", hour2, bytesReceived: 200, bytesSent: 75),
                CreateNetworkRecord("history", day.ToDateTime(TimeOnly.MinValue), bytesReceived: 999, bytesSent: 999, granularity: UsageGranularity.Daily));
            await db.SaveChangesAsync(cancellationToken);
        }

        // Act
        var result = await _service.GetNetworkHistoryAsync("history", day, day, cancellationToken);

        // Assert
        result.Should().HaveCount(2);
        result[0].Should().Be(new AppNetworkHistoryPoint(hour1, 100, 50));
        result[1].Should().Be(new AppNetworkHistoryPoint(hour2, 200, 75));
    }

    [Test, Timeout(30000)]
    public async Task GetResourceHistoryAsync_NoSnapshots_ReturnsEmptyList(CancellationToken cancellationToken)
    {
        // Arrange
        var day = DateOnly.FromDateTime(DateTime.Now);

        // Act
        var result = await _service.GetResourceHistoryAsync("missing", day, day, cancellationToken);

        // Assert
        result.Should().BeEmpty();
    }

    [Test, Timeout(30000)]
    public async Task GetTopDestinationsAsync_OrdersByTotalBytesDesc_RespectsLimit(CancellationToken cancellationToken)
    {
        // Arrange
        var day = DateOnly.FromDateTime(DateTime.Now);
        var hour = day.ToDateTime(new TimeOnly(7, 0));

        using (var scope = CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<WireBoundDbContext>();
            db.AddressUsageRecords.AddRange(
                CreateAddressRecord("10.0.0.1", 443, "TCP", hour, bytesSent: 50, bytesReceived: 50, hostname: "small.example"),
                CreateAddressRecord("10.0.0.2", 443, "TCP", hour, bytesSent: 500, bytesReceived: 500, hostname: "large.example"),
                CreateAddressRecord("10.0.0.2", 443, "TCP", hour.AddHours(1), bytesSent: 200, bytesReceived: 100, hostname: "large-new.example"),
                CreateAddressRecord("10.0.0.3", 53, "UDP", hour, bytesSent: 200, bytesReceived: 200, hostname: "medium.example"),
                CreateAddressRecord("10.0.0.4", 80, "TCP", day.ToDateTime(TimeOnly.MinValue), bytesSent: 9_999, bytesReceived: 9_999, granularity: UsageGranularity.Daily));
            await db.SaveChangesAsync(cancellationToken);
        }

        // Act
        var result = await _service.GetTopDestinationsAsync(2, day, day, cancellationToken);

        // Assert
        result.Should().HaveCount(2);
        result[0].Should().Be(new TopDestinationEntry("10.0.0.2", "large-new.example", 443, "TCP", 700, 600));
        result[1].Should().Be(new TopDestinationEntry("10.0.0.3", "medium.example", 53, "UDP", 200, 200));
    }

    [Test, Timeout(30000)]
    public async Task GetOverviewAsync_PerformsBoundedNumberOfQueries(CancellationToken cancellationToken)
    {
        // Arrange
        await using var connection = new SqliteConnection("DataSource=:memory:");
        await connection.OpenAsync(cancellationToken);
        var interceptor = new CountingCommandInterceptor();
        var services = new ServiceCollection();
        services.AddDbContext<WireBoundDbContext>(options =>
            options.UseSqlite(connection).AddInterceptors(interceptor));
        await using var serviceProvider = services.BuildServiceProvider();

        using (var scope = serviceProvider.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<WireBoundDbContext>();
            await db.Database.EnsureCreatedAsync(cancellationToken);
            var day = DateOnly.FromDateTime(DateTime.Now);
            var hour = day.ToDateTime(new TimeOnly(6, 0));

            for (var i = 0; i < 50; i++)
            {
                var appId = $"app-{i}";
                db.AppUsageRecords.Add(CreateNetworkRecord(appId, hour, bytesReceived: i + 1, bytesSent: i + 2));
                db.ResourceInsightSnapshots.Add(CreateResourceSnapshot(appId, hour, cpuPercent: i, privateBytes: i + 100));
            }

            await db.SaveChangesAsync(cancellationToken);
        }

        var service = new AppOverviewService(serviceProvider);
        interceptor.Reset();
        var start = Stopwatch.GetTimestamp();
        var range = DateOnly.FromDateTime(DateTime.Now);

        // Act
        var result = await service.GetOverviewAsync(range, range, cancellationToken);
        var elapsed = Stopwatch.GetElapsedTime(start);

        // Assert
        result.Should().HaveCount(50);
        interceptor.ReaderCommands.Should().BeLessThanOrEqualTo(2);
        elapsed.Should().BeLessThan(TimeSpan.FromSeconds(2));
    }

    private static AppUsageRecord CreateNetworkRecord(
        string appIdentifier,
        DateTime timestamp,
        long bytesReceived,
        long bytesSent,
        long peakDown = 0,
        long peakUp = 0,
        string appName = "Test App",
        string processName = "test.exe",
        string executablePath = "C:\\Apps\\test.exe",
        DateTime? lastUpdated = null,
        UsageGranularity granularity = UsageGranularity.Hourly)
    {
        return new AppUsageRecord
        {
            AppIdentifier = appIdentifier,
            AppName = appName,
            ProcessName = processName,
            ExecutablePath = executablePath,
            Timestamp = timestamp,
            Granularity = granularity,
            BytesReceived = bytesReceived,
            BytesSent = bytesSent,
            PeakDownloadSpeed = peakDown,
            PeakUploadSpeed = peakUp,
            LastUpdated = lastUpdated ?? timestamp
        };
    }

    private static ResourceInsightSnapshot CreateResourceSnapshot(
        string appIdentifier,
        DateTime timestamp,
        double cpuPercent,
        long privateBytes,
        double peakCpuPercent = 0,
        long peakPrivateBytes = 0,
        string appName = "Test App",
        string categoryName = "General",
        DateTime? lastUpdated = null,
        UsageGranularity granularity = UsageGranularity.Hourly)
    {
        return new ResourceInsightSnapshot
        {
            AppIdentifier = appIdentifier,
            AppName = appName,
            CategoryName = categoryName,
            Timestamp = timestamp,
            Granularity = granularity,
            CpuPercent = cpuPercent,
            PrivateBytes = privateBytes,
            PeakCpuPercent = peakCpuPercent,
            PeakPrivateBytes = peakPrivateBytes,
            LastUpdated = lastUpdated ?? timestamp
        };
    }

    private static AddressUsageRecord CreateAddressRecord(
        string remoteAddress,
        int port,
        string protocol,
        DateTime timestamp,
        long bytesSent,
        long bytesReceived,
        string? hostname = null,
        UsageGranularity granularity = UsageGranularity.Hourly)
    {
        return new AddressUsageRecord
        {
            RemoteAddress = remoteAddress,
            PrimaryPort = port,
            Protocol = protocol,
            Hostname = hostname,
            Timestamp = timestamp,
            Granularity = granularity,
            BytesSent = bytesSent,
            BytesReceived = bytesReceived,
            LastUpdated = timestamp
        };
    }

    private sealed class CountingCommandInterceptor : DbCommandInterceptor
    {
        public int ReaderCommands { get; private set; }

        public void Reset()
        {
            ReaderCommands = 0;
        }

        public override InterceptionResult<DbDataReader> ReaderExecuting(
            DbCommand command,
            CommandEventData eventData,
            InterceptionResult<DbDataReader> result)
        {
            ReaderCommands++;
            return base.ReaderExecuting(command, eventData, result);
        }

        public override ValueTask<InterceptionResult<DbDataReader>> ReaderExecutingAsync(
            DbCommand command,
            CommandEventData eventData,
            InterceptionResult<DbDataReader> result,
            CancellationToken cancellationToken = default)
        {
            ReaderCommands++;
            return base.ReaderExecutingAsync(command, eventData, result, cancellationToken);
        }
    }
}
