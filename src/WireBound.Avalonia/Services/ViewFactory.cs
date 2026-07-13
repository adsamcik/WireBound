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
            Routes.Settings => CreateSettingsView(),
            Routes.Apps => CreateAppsView(),
            Routes.Connections => CreateConnectionsView(),
            Routes.System => CreateSystemView(),
            Routes.History => CreateHistoryView(),
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

    private Control CreateSettingsView()
    {
        var view = _serviceProvider.GetRequiredService<SettingsView>();
        view.DataContext = _serviceProvider.GetRequiredService<SettingsViewModel>();
        return view;
    }

    private Control CreateAppsView()
    {
        var view = _serviceProvider.GetRequiredService<AppsView>();
        view.DataContext = _serviceProvider.GetRequiredService<AppsViewModel>();
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

    private Control CreateHistoryView()
    {
        var view = _serviceProvider.GetRequiredService<HistoryView>();
        view.DataContext = _serviceProvider.GetRequiredService<HistoryViewModel>();
        return view;
    }
}
