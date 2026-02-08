using Avalonia.Controls;
using Avalonia.Interactivity;

namespace WireBound.Avalonia.Views;

public partial class WhatsNewWindow : Window
{
    public WhatsNewWindow()
    {
        InitializeComponent();
    }

    public WhatsNewWindow(string version, string releaseNotes) : this()
    {
        TitleText.Text = $"What's New in v{version}";
        NotesText.Text = releaseNotes;
    }

    private void OnGotItClick(object? sender, RoutedEventArgs e)
    {
        Close();
    }
}
