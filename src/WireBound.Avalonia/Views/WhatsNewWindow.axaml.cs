using Avalonia.Controls;
using Avalonia.Interactivity;

namespace WireBound.Avalonia.Views;

public partial class WhatsNewWindow : Window
{
    public WhatsNewWindow() : this(string.Empty, string.Empty)
    {
    }

    public WhatsNewWindow(string version, string releaseNotes)
    {
        InitializeComponent();
        DataContext = new WhatsNewWindowViewModel(version, releaseNotes);
    }

    private void OnGotItClick(object? sender, RoutedEventArgs e)
    {
        Close();
    }

    private sealed class WhatsNewWindowViewModel
    {
        public string Title { get; }
        public string Notes { get; }
        public Uri LinkBaseUri { get; }

        public WhatsNewWindowViewModel(string version, string notes)
        {
            var normalizedVersion = version.Trim().TrimStart('v', 'V');
            Title = string.IsNullOrWhiteSpace(normalizedVersion) ? "What's New" : $"What's New in v{normalizedVersion}";
            Notes = notes;
            LinkBaseUri = string.IsNullOrWhiteSpace(normalizedVersion)
                ? new Uri("https://github.com/adsamcik/WireBound/blob/main/")
                : new Uri($"https://github.com/adsamcik/WireBound/blob/v{normalizedVersion}/");
        }
    }
}
