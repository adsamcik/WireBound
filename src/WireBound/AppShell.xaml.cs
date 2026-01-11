using Serilog;

namespace WireBound;

/// <summary>
/// Application Shell providing flyout navigation.
/// Uses MAUI Shell with locked flyout for desktop sidebar experience.
/// </summary>
public partial class AppShell : Shell
{
    public AppShell()
    {
        InitializeComponent();
        
        // Add navigation event handlers for debugging
        this.Navigating += OnShellNavigating;
        this.Navigated += OnShellNavigated;
        
        Log.Information("AppShell initialized. FlyoutItems count: {Count}", Items.Count);
    }
    
    private void OnShellNavigating(object? sender, ShellNavigatingEventArgs e)
    {
        Log.Debug("Shell navigating from {Source} to {Target}, type: {Type}", 
            e.Current?.Location?.OriginalString ?? "null", 
            e.Target?.Location?.OriginalString ?? "null",
            e.Source);
    }
    
    private void OnShellNavigated(object? sender, ShellNavigatedEventArgs e)
    {
        Log.Debug("Shell navigated. Previous: {Previous}, Current: {Current}, Source: {Source}",
            e.Previous?.Location?.OriginalString ?? "null",
            e.Current?.Location?.OriginalString ?? "null",
            e.Source);
    }
}
