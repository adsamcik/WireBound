using Avalonia.Controls;
using Microsoft.Extensions.DependencyInjection;
using WireBound.Avalonia.ViewModels;
using WireBound.Avalonia.Views;
using WireBound.Core;

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
            Routes.Overview => CreateOverviewView(),
            Routes.Charts => CreateChartsView(),
            Routes.Insights => CreateInsightsView(),
            Routes.Settings => CreateSettingsView(),
            Routes.Applications => CreateApplicationsView(),
            Routes.Connections => CreateConnectionsView(),
            Routes.System => CreateSystemView(),
            _ => CreateOverviewView()
        };
    }

    private Control CreateOverviewView()
    {
        var view = _serviceProvider.GetRequiredService<OverviewView>();
        view.DataContext = _serviceProvider.GetRequiredService<OverviewViewModel>();
        return view;
    }

    private Control CreateChartsView()
    {
        var view = _serviceProvider.GetRequiredService<ChartsView>();
        view.DataContext = _serviceProvider.GetRequiredService<ChartsViewModel>();
        return view;
    }

    private Control CreateInsightsView()
    {
        var view = _serviceProvider.GetRequiredService<InsightsView>();
        view.DataContext = _serviceProvider.GetRequiredService<InsightsViewModel>();
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

    private Control CreateConnectionsView()
    {
        var view = _serviceProvider.GetRequiredService<ConnectionsView>();
        view.DataContext = _serviceProvider.GetRequiredService<ConnectionsViewModel>();
        return view;
    }

    private Control CreateSystemView()
    {
        var view = _serviceProvider.GetRequiredService<SystemView>();
        view.DataContext = _serviceProvider.GetRequiredService<SystemViewModel>();
        return view;
    }
}
