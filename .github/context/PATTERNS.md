<!--
context-init:version: 3.0.0
context-init:generated: 2026-02-07T14:21:00Z
context-init:mode: full-init
-->

# Coding Patterns

## Naming Conventions

<!-- context-init:managed -->

| Type | Pattern | Example | Notes |
|------|---------|---------|-------|
| Files | PascalCase | `NetworkAdapter.cs` | Match class name |
| Namespaces | PascalCase | `WireBound.Core.Models` | Match folder structure |
| Classes | PascalCase | `NetworkMonitorService` | Nouns/noun phrases |
| Interfaces | I + PascalCase | `INetworkMonitorService` | Always prefix with `I` |
| Methods | PascalCase | `GetCurrentStats()` | Verb or verb phrases |
| Properties | PascalCase | `DownloadSpeedBps` | |
| Private fields | _camelCase | `_networkMonitor` | Underscore prefix |
| Constants | PascalCase | `Routes.Overview` | Not UPPER_SNAKE |
| Local variables | camelCase | `currentStats` | |
| Parameters | camelCase | `pollingInterval` | |
| Async methods | ...Async | `SaveSettingsAsync()` | Always suffix |
| ViewModels | ...ViewModel | `OverviewViewModel` | |
| Views | ...View / ...Window | `OverviewView` | |
| Tests | ...Tests | `OverviewViewModelTests` | |
| Platform services | Platform + Name | `WindowsCpuInfoProvider` | |
| Stubs | Stub + Name | `StubCpuInfoProvider` | |

**Where to see it**: Any file in `src/` follows these patterns consistently.

## MVVM Pattern

<!-- context-init:managed -->

### ViewModel Structure

```csharp
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace WireBound.Avalonia.ViewModels;

public partial class ExampleViewModel : ObservableObject, IDisposable
{
    private readonly INetworkMonitorService _networkMonitor;
    private bool _disposed;

    // Source-generated observable properties
    [ObservableProperty]
    private string _downloadSpeed = "0 B/s";

    [ObservableProperty]
    private bool _isLoading;

    public ExampleViewModel(INetworkMonitorService networkMonitor)
    {
        _networkMonitor = networkMonitor;
        _networkMonitor.StatsUpdated += OnStatsUpdated;
    }

    // Partial method for property change side effects
    partial void OnIsLoadingChanged(bool value) { }

    // Source-generated relay command
    [RelayCommand]
    private async Task RefreshAsync()
    {
        IsLoading = true;
        try { /* work */ }
        finally { IsLoading = false; }
    }

    private void OnStatsUpdated(object? sender, NetworkStats stats)
    {
        DownloadSpeed = ByteFormatter.Format(stats.DownloadSpeedBps);
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        _networkMonitor.StatsUpdated -= OnStatsUpdated;
    }
}
```

**Key points**:
- Class must be `partial` for source generators
- Use `[ObservableProperty]` on private fields (generates public PascalCase property)
- Use `[RelayCommand]` on methods (generates `...Command` property)
- Subscribe/unsubscribe from events in constructor/Dispose

**Where to see it**: `src/WireBound.Avalonia/ViewModels/OverviewViewModel.cs`, `MainViewModel.cs`

## Platform Abstraction Pattern

<!-- context-init:managed -->

### Three-layer implementation

1. **Interface** in `WireBound.Platform.Abstract/Services/`
2. **Platform implementations** in `Platform.Windows/Services/` and `Platform.Linux/Services/`
3. **Stub fallback** in `Platform.Stub/Services/`

```csharp
// 1. Interface (Platform.Abstract)
public interface ICpuInfoProvider
{
    Task<double> GetCpuUsagePercentAsync();
}

// 2. Windows implementation
[SupportedOSPlatform("windows")]
public sealed class WindowsCpuInfoProvider : ICpuInfoProvider { ... }

// 3. Linux implementation
[SupportedOSPlatform("linux")]
public sealed class LinuxCpuInfoProvider : ICpuInfoProvider { ... }

// 4. Stub (always required)
public sealed class StubCpuInfoProvider : ICpuInfoProvider
{
    public Task<double> GetCpuUsagePercentAsync() => Task.FromResult(0.0);
}
```

**Registration** (stubs first, platform overrides second):
```csharp
StubPlatformServices.Instance.Register(services);
if (OperatingSystem.IsWindows())
    WindowsPlatformServices.Instance.Register(services);
else if (OperatingSystem.IsLinux())
    LinuxPlatformServices.Instance.Register(services);
```

**Where to see it**: `src/WireBound.Platform.*/Services/` directories

## Dependency Injection Pattern

<!-- context-init:managed -->

### Registration order in `App.ConfigureServices()`

```csharp
// 1. Core infrastructure (logging, database)
services.AddDbContext<WireBoundDbContext>();

// 2. Core services
services.AddSingleton<INetworkMonitorService, CrossPlatformNetworkMonitorService>();

// 3. Stubs as defaults
StubPlatformServices.Instance.Register(services);

// 4. Platform overrides
if (OperatingSystem.IsWindows())
    WindowsPlatformServices.Instance.Register(services);

// 5. Interface segregation (one impl → many interfaces)
services.AddSingleton<DataPersistenceService>();
services.AddSingleton<IDataPersistenceService>(sp => sp.GetRequiredService<DataPersistenceService>());
services.AddSingleton<INetworkUsageRepository>(sp => sp.GetRequiredService<DataPersistenceService>());

// 6. ViewModels (singletons)
services.AddSingleton<MainViewModel>();

// 7. Views (transient — fresh instance per navigation)
services.AddTransient<OverviewView>();
```

**Where to see it**: `src/WireBound.Avalonia/App.axaml.cs`

## Testing Patterns

<!-- context-init:managed -->

### Test structure (TUnit + NSubstitute + AwesomeAssertions)

```csharp
public class OverviewViewModelTests : IAsyncDisposable
{
    private readonly INetworkMonitorService _networkMonitor;
    private OverviewViewModel? _viewModel;

    public OverviewViewModelTests()
    {
        _networkMonitor = Substitute.For<INetworkMonitorService>();
        SetupDefaultMocks();
    }

    private void SetupDefaultMocks()
    {
        _networkMonitor.GetCurrentStats().Returns(CreateDefaultStats());
    }

    private OverviewViewModel CreateViewModel()
    {
        return new OverviewViewModel(_networkMonitor, /* other deps */);
    }

    [Test]
    public async Task Constructor_InitializesDefaultSpeedValues()
    {
        _viewModel = CreateViewModel();
        _viewModel.DownloadSpeed.Should().Be("0 B/s");
    }

    public async ValueTask DisposeAsync()
    {
        _viewModel?.Dispose();
    }
}
```

**Key points**:
- Use `[Test]` attribute (TUnit), NOT `[Fact]` (xUnit)
- Use `Substitute.For<T>()` (NSubstitute), NOT `new Mock<T>()` (Moq)
- Use `.Should().Be()` assertions (AwesomeAssertions)
- Database tests extend `DatabaseTestBase` for in-memory EF Core setup
- Tests touching LiveCharts need `LiveChartsHook` assembly fixture

**Where to see it**: `tests/WireBound.Tests/ViewModels/`, `tests/WireBound.Tests/Fixtures/`

## Async Patterns

<!-- context-init:managed -->

### Fire-and-forget with error handling

```csharp
_ = InitializeAsyncServicesAsync();

private async Task InitializeAsyncServicesAsync()
{
    try { await StartBackgroundServicesAsync(); }
    catch (Exception ex) { Log.Error(ex, "Failed async initialization"); }
}
```

### UI thread updates (required for Avalonia/LiveCharts)

```csharp
await Dispatcher.UIThread.InvokeAsync(() =>
{
    ChartSeries.Add(newPoint);
});
```

### Background service error isolation

```csharp
while (!_cancellationToken.IsCancellationRequested)
{
    try
    {
        var stats = await _monitor.GetCurrentStatsAsync();
        StatsUpdated?.Invoke(this, stats);
    }
    catch (Exception ex)
    {
        Log.Warning(ex, "Polling iteration failed, continuing...");
    }
    await Task.Delay(_interval, _cancellationToken);
}
```

**Where to see it**: `src/WireBound.Avalonia/Services/NetworkPollingBackgroundService.cs`

## Error Handling

<!-- context-init:managed -->

### Standard pattern: catch, log, return safe default

```csharp
public async Task<AppSettings> LoadSettingsAsync()
{
    try { return await _repository.GetSettingsAsync(); }
    catch (Exception ex)
    {
        Log.Error(ex, "Failed to load settings");
        return new AppSettings(); // safe default
    }
}
```

No custom error types or Result pattern — uses try/catch with Serilog logging throughout.

**Where to see it**: Most service methods in `src/WireBound.Avalonia/Services/`

## AXAML Styling Patterns

<!-- context-init:managed -->

### Resource references

```xml
<Border Background="{StaticResource SurfaceBackground}">
    <TextBlock
        Text="{Binding DownloadSpeed}"
        Foreground="{StaticResource DownloadColor}"
        FontWeight="{StaticResource HeadingWeight}"/>
</Border>
```

### Custom control usage

```xml
<controls:CircularGauge
    Value="{Binding CpuUsagePercent}"
    StrokeColor="{StaticResource CpuColor}"
    Size="80"/>
```

### Accessibility (required for all interactive elements)

```xml
<Button
    Command="{Binding RefreshCommand}"
    AutomationProperties.Name="Refresh network statistics"
    AutomationProperties.HelpText="Click to refresh">
    ⟳
</Button>
```

**Where to see it**: `src/WireBound.Avalonia/Views/*.axaml`, `src/WireBound.Avalonia/Styles/`

## DateTime Usage

<!-- context-init:managed -->

This is a **local desktop app** — DateTime follows intentional patterns:

| Use `DateTime.Now` (Local) | Use `DateTime.UtcNow` |
|---|---|
| Hourly/daily aggregations | Cache TTL checks |
| Speed snapshot timestamps | Internal timing |
| UI date pickers/filters | |
| Data retention cleanup | |

```csharp
// ✅ Aggregations use local time
var today = DateOnly.FromDateTime(DateTime.Now);

// ✅ Cache TTL uses UTC
if (DateTime.UtcNow - entry.CachedAt < CacheTtl) return cached;
```

**Where to see it**: `DataPersistenceService.cs` (local), `DnsResolverService.cs` (UTC cache)

## Commit Conventions

<!-- context-init:managed -->

```
type(scope): description

feat(system): add CPU monitoring service
fix(charts): resolve threading issue on chart update
docs(readme): update build instructions
refactor(core): extract ByteFormatter to helper
test(viewmodels): add OverviewViewModel tests
chore(deps): update LiveCharts to 2.0.1
```

Types: `feat`, `fix`, `docs`, `style`, `refactor`, `test`, `chore`

<!-- context-init:user-content-below -->
