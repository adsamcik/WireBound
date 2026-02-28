using Avalonia;
using Avalonia.Controls.ApplicationLifetimes;
using WireBound.Core.Services;

namespace WireBound.Avalonia.Services;

public sealed class AvaloniaClipboardService : IClipboardService
{
    public async Task SetTextAsync(string text)
    {
        if (Application.Current?.ApplicationLifetime is not IClassicDesktopStyleApplicationLifetime desktop)
            return;

        var clipboard = desktop.MainWindow?.Clipboard;
        if (clipboard != null)
            await clipboard.SetTextAsync(text);
    }
}
