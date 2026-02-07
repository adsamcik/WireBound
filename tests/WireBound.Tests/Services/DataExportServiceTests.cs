using WireBound.Avalonia.Services;
using WireBound.Core.Models;
using WireBound.Core.Services;

namespace WireBound.Tests.Services;

/// <summary>
/// Unit tests for DataExportService CSV export functionality
/// </summary>
public class DataExportServiceTests : IAsyncDisposable
{
    private readonly INetworkUsageRepository _repository;
    private readonly DataExportService _service;
    private readonly string _tempDir;

    public DataExportServiceTests()
    {
        _repository = Substitute.For<INetworkUsageRepository>();
        _service = new DataExportService(_repository);
        _tempDir = Path.Combine(Path.GetTempPath(), $"wirebound-test-{Guid.NewGuid():N}");
        Directory.CreateDirectory(_tempDir);
    }

    public ValueTask DisposeAsync()
    {
        try { Directory.Delete(_tempDir, recursive: true); } catch { }
        GC.SuppressFinalize(this);
        return ValueTask.CompletedTask;
    }

    private string TempFile(string name) => Path.Combine(_tempDir, name);

    // ═══════════════════════════════════════════════════════════════════════
    // ExportDailyUsageToCsvAsync Tests
    // ═══════════════════════════════════════════════════════════════════════

    [Test]
    public async Task ExportDaily_WritesCorrectHeaders()
    {
        _repository.GetDailyUsageAsync(Arg.Any<DateOnly>(), Arg.Any<DateOnly>())
            .Returns(new List<DailyUsage>());

        var path = TempFile("daily-headers.csv");
        await _service.ExportDailyUsageToCsvAsync(path, DateOnly.MinValue, DateOnly.MaxValue);

        var content = await File.ReadAllTextAsync(path);
        content.Should().StartWith("Date,AdapterId,BytesReceived,BytesSent,TotalBytes,PeakDownloadSpeed,PeakUploadSpeed,ReceivedFormatted,SentFormatted");
    }

    [Test]
    public async Task ExportDaily_EmptyData_CreatesFileWithHeadersOnly()
    {
        _repository.GetDailyUsageAsync(Arg.Any<DateOnly>(), Arg.Any<DateOnly>())
            .Returns(new List<DailyUsage>());

        var path = TempFile("daily-empty.csv");
        await _service.ExportDailyUsageToCsvAsync(path, DateOnly.MinValue, DateOnly.MaxValue);

        var lines = await File.ReadAllLinesAsync(path);
        lines.Length.Should().Be(1); // Header only
    }

    [Test]
    public async Task ExportDaily_WithData_FormatsDateCorrectly()
    {
        var data = new List<DailyUsage>
        {
            new() { Date = new DateOnly(2026, 1, 15), AdapterId = "eth0", BytesReceived = 1000, BytesSent = 500 }
        };
        _repository.GetDailyUsageAsync(Arg.Any<DateOnly>(), Arg.Any<DateOnly>())
            .Returns(data);

        var path = TempFile("daily-date.csv");
        await _service.ExportDailyUsageToCsvAsync(path, DateOnly.MinValue, DateOnly.MaxValue);

        var content = await File.ReadAllTextAsync(path);
        content.Should().Contain("2026-01-15");
    }

    [Test]
    public async Task ExportDaily_WithData_IncludesAllFields()
    {
        var data = new List<DailyUsage>
        {
            new()
            {
                Date = new DateOnly(2026, 2, 1),
                AdapterId = "adapter1",
                BytesReceived = 1_000_000,
                BytesSent = 500_000,
                PeakDownloadSpeed = 10_000,
                PeakUploadSpeed = 5_000
            }
        };
        _repository.GetDailyUsageAsync(Arg.Any<DateOnly>(), Arg.Any<DateOnly>())
            .Returns(data);

        var path = TempFile("daily-fields.csv");
        await _service.ExportDailyUsageToCsvAsync(path, DateOnly.MinValue, DateOnly.MaxValue);

        var lines = await File.ReadAllLinesAsync(path);
        lines.Length.Should().Be(2); // Header + 1 data row

        var dataLine = lines[1];
        dataLine.Should().Contain("adapter1");
        dataLine.Should().Contain("1000000");
        dataLine.Should().Contain("500000");
        dataLine.Should().Contain("1500000"); // TotalBytes
        dataLine.Should().Contain("10000");
        dataLine.Should().Contain("5000");
    }

    [Test]
    public async Task ExportDaily_EscapesCommasInAdapterId()
    {
        var data = new List<DailyUsage>
        {
            new() { Date = new DateOnly(2026, 1, 1), AdapterId = "adapter,with,commas", BytesReceived = 100, BytesSent = 50 }
        };
        _repository.GetDailyUsageAsync(Arg.Any<DateOnly>(), Arg.Any<DateOnly>())
            .Returns(data);

        var path = TempFile("daily-escape.csv");
        await _service.ExportDailyUsageToCsvAsync(path, DateOnly.MinValue, DateOnly.MaxValue);

        var content = await File.ReadAllTextAsync(path);
        content.Should().Contain("\"adapter,with,commas\"");
    }

    [Test]
    public async Task ExportDaily_EscapesQuotesInAdapterId()
    {
        var data = new List<DailyUsage>
        {
            new() { Date = new DateOnly(2026, 1, 1), AdapterId = "adapter\"quoted", BytesReceived = 100, BytesSent = 50 }
        };
        _repository.GetDailyUsageAsync(Arg.Any<DateOnly>(), Arg.Any<DateOnly>())
            .Returns(data);

        var path = TempFile("daily-quote.csv");
        await _service.ExportDailyUsageToCsvAsync(path, DateOnly.MinValue, DateOnly.MaxValue);

        var content = await File.ReadAllTextAsync(path);
        content.Should().Contain("\"adapter\"\"quoted\"");
    }

    [Test]
    public async Task ExportDaily_OrdersByDate()
    {
        var data = new List<DailyUsage>
        {
            new() { Date = new DateOnly(2026, 1, 3), AdapterId = "a", BytesReceived = 3, BytesSent = 0 },
            new() { Date = new DateOnly(2026, 1, 1), AdapterId = "a", BytesReceived = 1, BytesSent = 0 },
            new() { Date = new DateOnly(2026, 1, 2), AdapterId = "a", BytesReceived = 2, BytesSent = 0 }
        };
        _repository.GetDailyUsageAsync(Arg.Any<DateOnly>(), Arg.Any<DateOnly>())
            .Returns(data);

        var path = TempFile("daily-order.csv");
        await _service.ExportDailyUsageToCsvAsync(path, DateOnly.MinValue, DateOnly.MaxValue);

        var lines = await File.ReadAllLinesAsync(path);
        lines[1].Should().StartWith("2026-01-01");
        lines[2].Should().StartWith("2026-01-02");
        lines[3].Should().StartWith("2026-01-03");
    }

    // ═══════════════════════════════════════════════════════════════════════
    // ExportHourlyUsageToCsvAsync Tests
    // ═══════════════════════════════════════════════════════════════════════

    [Test]
    public async Task ExportHourly_WritesCorrectHeaders()
    {
        _repository.GetHourlyUsageAsync(Arg.Any<DateOnly>())
            .Returns(new List<HourlyUsage>());

        var path = TempFile("hourly-headers.csv");
        await _service.ExportHourlyUsageToCsvAsync(path, DateOnly.FromDateTime(DateTime.Today));

        var content = await File.ReadAllTextAsync(path);
        content.Should().StartWith("Hour,AdapterId,BytesReceived,BytesSent,PeakDownloadSpeed,PeakUploadSpeed,ReceivedFormatted,SentFormatted");
    }

    [Test]
    public async Task ExportHourly_EmptyData_CreatesFileWithHeadersOnly()
    {
        _repository.GetHourlyUsageAsync(Arg.Any<DateOnly>())
            .Returns(new List<HourlyUsage>());

        var path = TempFile("hourly-empty.csv");
        await _service.ExportHourlyUsageToCsvAsync(path, DateOnly.FromDateTime(DateTime.Today));

        var lines = await File.ReadAllLinesAsync(path);
        lines.Length.Should().Be(1);
    }

    [Test]
    public async Task ExportHourly_WithData_FormatsHourCorrectly()
    {
        var data = new List<HourlyUsage>
        {
            new() { Hour = new DateTime(2026, 2, 1, 14, 0, 0), AdapterId = "eth0", BytesReceived = 500, BytesSent = 200 }
        };
        _repository.GetHourlyUsageAsync(Arg.Any<DateOnly>())
            .Returns(data);

        var path = TempFile("hourly-format.csv");
        await _service.ExportHourlyUsageToCsvAsync(path, new DateOnly(2026, 2, 1));

        var content = await File.ReadAllTextAsync(path);
        content.Should().Contain("2026-02-01 14:00");
    }

    // ═══════════════════════════════════════════════════════════════════════
    // ExportDailyUsageToCsvAsync – Additional Coverage
    // ═══════════════════════════════════════════════════════════════════════

    [Test]
    public async Task ExportDailyUsageToCsvAsync_CreatesValidCsvFile()
    {
        var data = new List<DailyUsage>
        {
            new()
            {
                Date = new DateOnly(2026, 3, 15),
                AdapterId = "eth0",
                BytesReceived = 1_073_741_824,
                BytesSent = 536_870_912,
                PeakDownloadSpeed = 125_000_000,
                PeakUploadSpeed = 62_500_000
            },
            new()
            {
                Date = new DateOnly(2026, 3, 16),
                AdapterId = "wlan0",
                BytesReceived = 2_000_000,
                BytesSent = 1_000_000,
                PeakDownloadSpeed = 50_000,
                PeakUploadSpeed = 25_000
            }
        };
        _repository.GetDailyUsageAsync(Arg.Any<DateOnly>(), Arg.Any<DateOnly>())
            .Returns(data);

        var path = TempFile("daily-valid.csv");
        await _service.ExportDailyUsageToCsvAsync(path, new DateOnly(2026, 3, 15), new DateOnly(2026, 3, 16));

        File.Exists(path).Should().BeTrue();

        var lines = await File.ReadAllLinesAsync(path);
        lines.Length.Should().Be(3); // header + 2 data rows

        // Each data row should have 9 fields (matching the 9-column header)
        var headerFields = lines[0].Split(',');
        headerFields.Length.Should().Be(9);

        lines[1].Should().Contain("eth0");
        lines[2].Should().Contain("wlan0");
    }

    [Test]
    public async Task ExportDailyUsageToCsvAsync_FormatsNumbersCorrectly()
    {
        // Use large numbers that would be affected by culture-specific thousand separators
        var data = new List<DailyUsage>
        {
            new()
            {
                Date = new DateOnly(2026, 6, 1),
                AdapterId = "eth0",
                BytesReceived = 10_737_418_240,   // ~10 GB
                BytesSent = 5_368_709_120,        // ~5 GB
                PeakDownloadSpeed = 125_000_000,  // 125 MB/s
                PeakUploadSpeed = 62_500_000      // 62.5 MB/s
            }
        };
        _repository.GetDailyUsageAsync(Arg.Any<DateOnly>(), Arg.Any<DateOnly>())
            .Returns(data);

        var path = TempFile("daily-numbers.csv");
        await _service.ExportDailyUsageToCsvAsync(path, DateOnly.MinValue, DateOnly.MaxValue);

        var lines = await File.ReadAllLinesAsync(path);
        var dataLine = lines[1];

        // Numbers must use InvariantCulture (no thousand separators, dot for decimals)
        dataLine.Should().Contain("10737418240");
        dataLine.Should().Contain("5368709120");
        dataLine.Should().Contain("125000000");
        dataLine.Should().Contain("62500000");
        // TotalBytes should be sum without separators
        dataLine.Should().Contain("16106127360");
    }

    [Test]
    public async Task ExportDailyUsageToCsvAsync_HandlesEmptyData()
    {
        _repository.GetDailyUsageAsync(Arg.Any<DateOnly>(), Arg.Any<DateOnly>())
            .Returns(new List<DailyUsage>());

        var path = TempFile("daily-empty-check.csv");
        await _service.ExportDailyUsageToCsvAsync(path, DateOnly.MinValue, DateOnly.MaxValue);

        File.Exists(path).Should().BeTrue();
        var lines = await File.ReadAllLinesAsync(path);
        lines.Length.Should().Be(1); // Header only, no data rows
        lines[0].Should().StartWith("Date,");
    }

    // ═══════════════════════════════════════════════════════════════════════
    // BackupDatabaseAsync
    // NOTE: BackupDatabaseAsync is not part of IDataExportService / DataExportService.
    // It lives in SettingsViewModel as a [RelayCommand] that directly uses SqliteConnection.
    // Backup tests belong in SettingsViewModelTests, not here.
    // ═══════════════════════════════════════════════════════════════════════
}
