using LiveChartsCore;
using LiveChartsCore.SkiaSharpView;

namespace WireBound.Tests.Fixtures;

/// <summary>
/// Assembly-level fixture that initializes LiveCharts once before any tests run.
/// This prevents thread-safety issues with LiveCharts' static initializer.
/// </summary>
public class LiveChartsFixture : IDisposable
{
    private static readonly object _initLock = new();
    private static bool _initialized;

    public LiveChartsFixture()
    {
        EnsureInitialized();
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

    public void Dispose()
    {
        // Nothing to dispose
    }
}

/// <summary>
/// Collection definition that runs tests using LiveCharts sequentially.
/// </summary>
[CollectionDefinition("LiveCharts")]
public class LiveChartsCollection : ICollectionFixture<LiveChartsFixture>
{
    // This class has no code, it's just a marker for xUnit
}
