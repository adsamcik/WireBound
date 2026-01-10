using WireBound.Maui.ViewModels;

namespace WireBound.Maui.Views;

public partial class MainPage : ContentPage
{
    private readonly DashboardViewModel _dashboardViewModel;
    private readonly HistoryViewModel _historyViewModel;
    private readonly SettingsViewModel _settingsViewModel;
    
    private DashboardView? _dashboardView;
    private HistoryView? _historyView;
    private SettingsView? _settingsView;
    
    private string _currentPage = "";
    private bool _loaded;

    public MainPage(
        DashboardViewModel dashboardViewModel,
        HistoryViewModel historyViewModel,
        SettingsViewModel settingsViewModel)
    {
        InitializeComponent();
        
        _dashboardViewModel = dashboardViewModel;
        _historyViewModel = historyViewModel;
        _settingsViewModel = settingsViewModel;
        
        // Show Dashboard by default after page loads
        Loaded += OnPageLoaded;
    }

    private void OnPageLoaded(object? sender, EventArgs e)
    {
        if (_loaded) return;
        _loaded = true;
        NavigateTo("Dashboard");
    }

    private void OnDashboardTapped(object? sender, TappedEventArgs e)
    {
        NavigateTo("Dashboard");
    }

    private void OnHistoryTapped(object? sender, TappedEventArgs e)
    {
        NavigateTo("History");
    }

    private void OnSettingsTapped(object? sender, TappedEventArgs e)
    {
        NavigateTo("Settings");
    }

    private void NavigateTo(string page)
    {
        if (_currentPage == page) return;
        _currentPage = page;
        
        // Update navigation styling
        UpdateNavStyles(page);
        
        // Update content using ContentViews (not ContentPages)
        ContentArea.Content = page switch
        {
            "Dashboard" => GetDashboardView(),
            "History" => GetHistoryView(),
            "Settings" => GetSettingsView(),
            _ => GetDashboardView()
        };
    }

    private DashboardView GetDashboardView()
    {
        _dashboardView ??= new DashboardView { BindingContext = _dashboardViewModel };
        return _dashboardView;
    }

    private HistoryView GetHistoryView()
    {
        _historyView ??= new HistoryView { BindingContext = _historyViewModel };
        return _historyView;
    }

    private SettingsView GetSettingsView()
    {
        _settingsView ??= new SettingsView { BindingContext = _settingsViewModel };
        return _settingsView;
    }

    private void UpdateNavStyles(string activePage)
    {
        var inactiveColor = Color.FromArgb("#999999");
        var activeBackgroundColor = Color.FromArgb("#00d4ff");
        var activeForegroundColor = Color.FromArgb("#1A1A2E");
        
        // Reset all nav items
        DashboardNav.BackgroundColor = Colors.Transparent;
        HistoryNav.BackgroundColor = Colors.Transparent;
        SettingsNav.BackgroundColor = Colors.Transparent;
        
        SetNavLabelStyle(DashboardNav, inactiveColor, FontAttributes.None);
        HistoryLabel.TextColor = inactiveColor;
        HistoryLabel.FontAttributes = FontAttributes.None;
        SettingsLabel.TextColor = inactiveColor;
        SettingsLabel.FontAttributes = FontAttributes.None;
        
        // Set active nav item
        switch (activePage)
        {
            case "Dashboard":
                DashboardNav.BackgroundColor = activeBackgroundColor;
                SetNavLabelStyle(DashboardNav, activeForegroundColor, FontAttributes.Bold);
                break;
            case "History":
                HistoryNav.BackgroundColor = activeBackgroundColor;
                HistoryLabel.TextColor = activeForegroundColor;
                HistoryLabel.FontAttributes = FontAttributes.Bold;
                break;
            case "Settings":
                SettingsNav.BackgroundColor = activeBackgroundColor;
                SettingsLabel.TextColor = activeForegroundColor;
                SettingsLabel.FontAttributes = FontAttributes.Bold;
                break;
        }
    }

    private static void SetNavLabelStyle(Border nav, Color color, FontAttributes attributes)
    {
        if (nav.Content is not HorizontalStackLayout stack) return;
        
        foreach (var child in stack.Children)
        {
            if (child is Label { Text: "Dashboard" } label)
            {
                label.TextColor = color;
                label.FontAttributes = attributes;
            }
        }
    }
}
