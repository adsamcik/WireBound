using System.Globalization;
using System.Text;
using Microsoft.Extensions.Logging;
using WireBound.Core.Helpers;
using WireBound.Core.Services;

namespace WireBound.Avalonia.Services;

/// <summary>
/// Exports usage data to CSV format for user backup and analysis.
/// </summary>
public sealed class DataExportService : IDataExportService
{
    private readonly INetworkUsageRepository _usageRepository;
    private readonly ILogger<DataExportService>? _logger;

    public DataExportService(INetworkUsageRepository usageRepository, ILogger<DataExportService>? logger = null)
    {
        _usageRepository = usageRepository;
        _logger = logger;
    }

    public async Task ExportDailyUsageToCsvAsync(string filePath, DateOnly startDate, DateOnly endDate)
    {
        _logger?.LogInformation("Exporting daily usage from {Start} to {End} to {Path}", startDate, endDate, filePath);

        var data = await _usageRepository.GetDailyUsageAsync(startDate, endDate);

        var sb = new StringBuilder();
        sb.AppendLine("Date,AdapterId,BytesReceived,BytesSent,TotalBytes,PeakDownloadSpeed,PeakUploadSpeed,ReceivedFormatted,SentFormatted");

        if (data is { Count: > 0 })
        {
            foreach (var row in data.OrderBy(d => d.Date))
            {
                sb.AppendLine(string.Format(
                    CultureInfo.InvariantCulture,
                    "{0},{1},{2},{3},{4},{5},{6},{7},{8}",
                    row.Date.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture),
                    EscapeCsv(row.AdapterId),
                    row.BytesReceived,
                    row.BytesSent,
                    row.TotalBytes,
                    row.PeakDownloadSpeed,
                    row.PeakUploadSpeed,
                    ByteFormatter.FormatBytes(row.BytesReceived),
                    ByteFormatter.FormatBytes(row.BytesSent)));
            }
        }

        await File.WriteAllTextAsync(filePath, sb.ToString());
        _logger?.LogInformation("Exported {Count} daily records to {Path}", data?.Count ?? 0, filePath);
    }

    public async Task ExportHourlyUsageToCsvAsync(string filePath, DateOnly date)
    {
        _logger?.LogInformation("Exporting hourly usage for {Date} to {Path}", date, filePath);

        var data = await _usageRepository.GetHourlyUsageAsync(date);

        var sb = new StringBuilder();
        sb.AppendLine("Hour,AdapterId,BytesReceived,BytesSent,PeakDownloadSpeed,PeakUploadSpeed,ReceivedFormatted,SentFormatted");

        if (data is { Count: > 0 })
        {
            foreach (var row in data.OrderBy(h => h.Hour))
            {
                sb.AppendLine(string.Format(
                    CultureInfo.InvariantCulture,
                    "{0},{1},{2},{3},{4},{5},{6},{7}",
                    row.Hour.ToString("yyyy-MM-dd HH:mm", CultureInfo.InvariantCulture),
                    EscapeCsv(row.AdapterId),
                    row.BytesReceived,
                    row.BytesSent,
                    row.PeakDownloadSpeed,
                    row.PeakUploadSpeed,
                    ByteFormatter.FormatBytes(row.BytesReceived),
                    ByteFormatter.FormatBytes(row.BytesSent)));
            }
        }

        await File.WriteAllTextAsync(filePath, sb.ToString());
        _logger?.LogInformation("Exported {Count} hourly records to {Path}", data?.Count ?? 0, filePath);
    }

    private static string EscapeCsv(string value)
    {
        if (value.Contains(',') || value.Contains('"') || value.Contains('\n'))
        {
            return $"\"{value.Replace("\"", "\"\"")}\"";
        }
        return value;
    }
}
