using WireBound.Maui.Helpers;
using WireBound.Maui.Models;

namespace WireBound.Maui.ViewModels;

/// <summary>
/// View model item for displaying DailyUsage in a list with formatted properties
/// </summary>
public class DailyUsageItem
{
    public DateOnly Date { get; }
    public long BytesReceived { get; }
    public long BytesSent { get; }
    public long TotalBytes { get; }

    public string DateFormatted => Date.ToString("MMM dd, yyyy");
    public string DownloadFormatted => ByteFormatter.FormatBytes(BytesReceived);
    public string UploadFormatted => ByteFormatter.FormatBytes(BytesSent);
    public string TotalFormatted => ByteFormatter.FormatBytes(TotalBytes);

    public DailyUsageItem(DailyUsage usage)
    {
        Date = usage.Date;
        BytesReceived = usage.BytesReceived;
        BytesSent = usage.BytesSent;
        TotalBytes = usage.TotalBytes;
    }
}
