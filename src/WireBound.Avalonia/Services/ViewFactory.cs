using Avalonia.Controls;
using Microsoft.Extensions.DependencyInjection;
using WireBound.Avalonia.ViewModels;
using WireBound.Avalonia.Views;

namespace WireBound.Avalonia.Services;

/// <summary>
/// DI-based factory for creating Views with their associated ViewModels.
/// </summary>
public sealed class ViewFactory : IViewFactory
{
    private readonly IServiceProvider _serviceProvider;

    public ViewFactory(IServiceProvider serviceProvider)
    {
        _serviceProvider = serviceProvider;
    }

    public Control CreateView(string route)
    {
        return route switch
        {
            "Dashboard" => CreateDashboardView(),
            "Charts" => CreateChartsView(),
            "History" => CreateHistoryView(),
            "Settings" => CreateSettingsView(),
            "Applications" => CreateApplicationsView(),
            _ => CreateDashboardView()
        };
    }

    private Control CreateDashboardView()
    {
        var view = _serviceProvider.GetRequiredService<DashboardView>();
        view.DataContext = _serviceProvider.GetRequiredService<DashboardViewModel>();
        return view;
    }

    private Control CreateChartsView()
    {
        var view = _serviceProvider.GetRequiredService<ChartsView>();
        view.DataContext = _serviceProvider.GetRequiredService<ChartsViewModel>();
        return view;
    }

    private Control CreateHistoryView()
    {
        var view = _serviceProvider.GetRequiredService<HistoryView>();
        view.DataContext = _serviceProvider.GetRequiredService<HistoryViewModel>();
        return view;
    }

    private Control CreateSettingsView()
    {
        var view = _serviceProvider.GetRequiredService<SettingsView>();
        view.DataContext = _serviceProvider.GetRequiredService<SettingsViewModel>();
        return view;
    }

    private Control CreateApplicationsView()
    {
        var view = _serviceProvider.GetRequiredService<ApplicationsView>();
        view.DataContext = _serviceProvider.GetRequiredService<ApplicationsViewModel>();
        return view;
    }
}
