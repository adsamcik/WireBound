namespace WireBound.Extensions;

/// <summary>
/// XAML markup extension for accessing localized strings.
/// Usage: Text="{local:Localize Settings_Title}"
/// </summary>
[ContentProperty(nameof(Key))]
public sealed class LocalizeExtension : IMarkupExtension<string>
{
    /// <summary>
    /// The resource key to look up.
    /// </summary>
    public string Key { get; set; } = string.Empty;

    public string ProvideValue(IServiceProvider serviceProvider)
    {
        if (string.IsNullOrEmpty(Key))
        {
            return string.Empty;
        }

        return Services.Strings.Get(Key);
    }

    object IMarkupExtension.ProvideValue(IServiceProvider serviceProvider)
    {
        return ProvideValue(serviceProvider);
    }
}
