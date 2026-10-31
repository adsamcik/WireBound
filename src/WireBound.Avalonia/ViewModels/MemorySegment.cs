using WireBound.Core.Helpers;

namespace WireBound.Avalonia.ViewModels;

/// <summary>One non-overlapping slice of physical capacity and its contributing consumers.</summary>
public sealed record MemorySegment(string Label, long Bytes, double Percent, string Color,
    bool UseLightText, IReadOnlyList<string> ConsumerIds, string Description)
{
    public string? ConsumerId => ConsumerIds.FirstOrDefault();
    public string Tooltip => $"{Label} · {ByteFormatter.FormatBytes(Bytes)} · {Percent:0.#}% of RAM\n{Description}"
        + (ConsumerId is not null ? "\nShow in memory consumers" : "");
}
