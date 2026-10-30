using WireBound.Core.Helpers;

namespace WireBound.Avalonia.ViewModels;

/// <summary>One non-overlapping slice of physical capacity, with an optional list destination.</summary>
public sealed record MemorySegment(string Label, long Bytes, double Percent, string Color,
    bool UseLightText, string? ConsumerId, string Description)
{
    public string Tooltip => $"{Label} · {ByteFormatter.FormatBytes(Bytes)} · {Percent:0.#}% of RAM\n{Description}"
        + (ConsumerId is not null ? "\nShow in memory consumers" : "");
}
