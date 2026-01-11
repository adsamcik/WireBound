using Serilog;
using WireBound.ViewModels;

namespace WireBound.Views;

/// <summary>
/// Charts page showing real-time network speed visualization.
/// Uses constructor injection for ViewModel - MAUI Shell resolves DI-registered pages automatically.
/// </summary>
public partial class ChartsPage : ContentPage
{
    public ChartsPage(DashboardViewModel viewModel)
    {
        try
        {
            Log.Information("ChartsPage constructor called");
            InitializeComponent();
            Log.Information("ChartsPage InitializeComponent completed");
            BindingContext = viewModel;
            Log.Information("ChartsPage BindingContext set");
            
            // Wire up chart interaction events for auto-pause detection
            SetupChartInteractions(viewModel);
            Log.Information("ChartsPage initialized successfully");
        }
        catch (Exception ex)
        {
            Log.Error(ex, "ChartsPage constructor failed: {Message}", ex.Message);
            throw;
        }
    }
    
    private void SetupChartInteractions(DashboardViewModel viewModel)
    {
        try
        {
            // Add gesture recognizers for auto-pause detection
            var panGesture = new PanGestureRecognizer();
            panGesture.PanUpdated += (s, args) =>
            {
                if (args.StatusType == GestureStatus.Running)
                {
                    viewModel.NotifyChartInteraction();
                }
            };
            SpeedChart.GestureRecognizers.Add(panGesture);
            
            var pointerGesture = new PointerGestureRecognizer();
            pointerGesture.PointerPressed += (s, args) =>
            {
                viewModel.NotifyChartInteraction();
            };
            SpeedChart.GestureRecognizers.Add(pointerGesture);
        }
        catch (Exception ex)
        {
            Log.Error(ex, "SetupChartInteractions failed: {Message}", ex.Message);
        }
    }
}
