namespace WireBound.Core.Services;

/// <summary>
/// Service abstraction for clipboard operations.
/// </summary>
public interface IClipboardService
{
    /// <summary>
    /// Copies text to the system clipboard.
    /// </summary>
    Task SetTextAsync(string text);
}
