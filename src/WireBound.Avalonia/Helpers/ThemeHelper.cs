using Avalonia;
using Avalonia.Styling;

namespace WireBound.Avalonia.Helpers;

public static class ThemeHelper
{
    public static void ApplyTheme(string theme)
    {
        if (Application.Current is not { } app) return;

        app.RequestedThemeVariant = theme switch
        {
            "Light" => ThemeVariant.Light,
            "System" => ThemeVariant.Default,
            _ => ThemeVariant.Dark
        };
    }
}
