using WireBound.Core.Models;

namespace WireBound.Core.Services;

/// <summary>
/// Service for exporting usage data to various formats.
/// </summary>
public interface IDataExportService
{
    /// <summary>
    /// Export daily usage data to CSV format.
    /// </summary>
    /// <param name="filePath">Path to write the CSV file.</param>
    /// <param name="startDate">Start date for the export range.</param>
    /// <param name="endDate">End date for the export range.</param>
    Task ExportDailyUsageToCsvAsync(string filePath, DateOnly startDate, DateOnly endDate);

    /// <summary>
    /// Export hourly usage data for a specific date to CSV format.
    /// </summary>
    /// <param name="filePath">Path to write the CSV file.</param>
    /// <param name="date">Date to export hourly data for.</param>
    Task ExportHourlyUsageToCsvAsync(string filePath, DateOnly date);
}
