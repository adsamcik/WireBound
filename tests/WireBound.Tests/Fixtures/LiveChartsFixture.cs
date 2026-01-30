using LiveChartsCore;
using LiveChartsCore.SkiaSharpView;

namespace WireBound.Tests.Fixtures;

/// <summary>
/// Assembly-level hook that initializes LiveCharts once before any tests run.
/// This prevents thread-safety issues with LiveCharts' static initializer.
/// </summary>
public class LiveChartsHook
{
    private static readonly object _initLock = new();
    private static bool _initialized;

    [Before(Assembly)]
    public static Task InitializeLiveCharts()
    {
        EnsureInitialized();
        return Task.CompletedTask;
    }

    public static void EnsureInitialized()
    {
        if (_initialized) return;

        lock (_initLock)
        {
            if (_initialized) return;

            // Force LiveCharts to initialize on a single thread
            LiveCharts.Configure(settings =>
            {
                settings.UseDefaults();
            });

            _initialized = true;
        }
    }
}
