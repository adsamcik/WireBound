# Coding Patterns

## Naming Conventions

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

## MVVM Pattern

### ViewModel Structure

```csharp
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace WireBound.Avalonia.ViewModels;

/// <summary>
/// Summary of what this ViewModel manages.
/// </summary>
public partial class ExampleViewModel : ObservableObject, IDisposable
{
    private readonly INetworkMonitorService _networkMonitor;
    private bool _disposed;

    // Observable properties use source generators
    [ObservableProperty]
    private string _downloadSpeed = "0 B/s";

    [ObservableProperty]
    private bool _isLoading;

    public ExampleViewModel(INetworkMonitorService networkMonitor)
    {
        _networkMonitor = networkMonitor;
        
        // Subscribe to events
        _networkMonitor.StatsUpdated += OnStatsUpdated;
    }

    // Partial method for property change side effects
    partial void OnIsLoadingChanged(bool value)
    {
        // React to property changes
    }

    // Commands use source generators
    [RelayCommand]
    private async Task RefreshAsync()
    {
        IsLoading = true;
        try
        {
            // Do work
        }
        finally
        {
            IsLoading = false;
        }
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

### Where to See It
- `src/WireBound.Avalonia/ViewModels/MainViewModel.cs`
- `src/WireBound.Avalonia/ViewModels/OverviewViewModel.cs`

## Platform Abstraction Pattern

### Interface Definition (Platform.Abstract)

```csharp
namespace WireBound.Platform.Abstract.Services;

/// <summary>
/// Provides CPU usage information.
/// </summary>
public interface ICpuInfoProvider
{
    /// <summary>
    /// Gets current CPU usage as percentage (0-100).
    /// </summary>
    Task<double> GetCpuUsagePercentAsync();
}
```

### Platform Implementation (Platform.Windows)

```csharp
using System.Runtime.Versioning;
using WireBound.Platform.Abstract.Services;

namespace WireBound.Platform.Windows.Services;

[SupportedOSPlatform("windows")]
public sealed class WindowsCpuInfoProvider : ICpuInfoProvider
{
    public async Task<double> GetCpuUsagePercentAsync()
    {
        // Windows-specific implementation using PerformanceCounter
    }
}
```

### Stub Implementation (Platform.Stub)

```csharp
using WireBound.Platform.Abstract.Services;

namespace WireBound.Platform.Stub.Services;

/// <summary>
/// Stub implementation for unsupported platforms or testing.
/// </summary>
public sealed class StubCpuInfoProvider : ICpuInfoProvider
{
    public Task<double> GetCpuUsagePercentAsync() => Task.FromResult(0.0);
}
```

### Registration (Platform.Windows)

```csharp
[SupportedOSPlatform("windows")]
public sealed class WindowsPlatformServices : IPlatformServices
{
    public static readonly WindowsPlatformServices Instance = new();
    
    public void Register(IServiceCollection services)
    {
        services.AddSingleton<ICpuInfoProvider, WindowsCpuInfoProvider>();
        // ... other services
    }
}
```

## Dependency Injection Pattern

### Service Registration Order

```csharp
// 1. Register core services first
services.AddDbContext<WireBoundDbContext>();
services.AddSingleton<INetworkMonitorService, CrossPlatformNetworkMonitorService>();

// 2. Register stubs as defaults
StubPlatformServices.Instance.Register(services);

// 3. Override with platform-specific implementations
if (OperatingSystem.IsWindows())
    WindowsPlatformServices.Instance.Register(services);
else if (OperatingSystem.IsLinux())
    LinuxPlatformServices.Instance.Register(services);

// 4. Register ViewModels (usually singletons)
services.AddSingleton<MainViewModel>();

// 5. Register Views (usually transient)
services.AddTransient<OverviewView>();
```

### Interface Segregation for Repositories

```csharp
// Register impl once, expose multiple interfaces
services.AddSingleton<DataPersistenceService>();
services.AddSingleton<IDataPersistenceService>(sp => sp.GetRequiredService<DataPersistenceService>());
services.AddSingleton<INetworkUsageRepository>(sp => sp.GetRequiredService<DataPersistenceService>());
services.AddSingleton<ISettingsRepository>(sp => sp.GetRequiredService<DataPersistenceService>());
```

## Async Patterns

### Fire-and-Forget with Error Handling

```csharp
// Use for async init that shouldn't block
_ = InitializeAsyncServicesAsync();

private async Task InitializeAsyncServicesAsync()
{
    try
    {
        await StartBackgroundServicesAsync();
    }
    catch (Exception ex)
    {
        Log.Error(ex, "Failed async initialization");
    }
}
```

### UI Thread Updates

```csharp
// Always update Avalonia UI from UI thread
await Dispatcher.UIThread.InvokeAsync(() =>
{
    ChartSeries.Add(newPoint);
});
```

## Testing Patterns

### Test Structure

```csharp
[Collection("LiveCharts")]  // Collection for shared context
public class OverviewViewModelTests : IDisposable
{
    private readonly Mock<INetworkMonitorService> _networkMonitorMock;
    private OverviewViewModel? _viewModel;

    public OverviewViewModelTests()
    {
        _networkMonitorMock = new Mock<INetworkMonitorService>();
        SetupDefaultMocks();
    }

    private void SetupDefaultMocks()
    {
        _networkMonitorMock
            .Setup(x => x.GetCurrentStats())
            .Returns(CreateDefaultNetworkStats());
    }

    private OverviewViewModel CreateViewModel()
    {
        return new OverviewViewModel(
            _networkMonitorMock.Object,
            // ... other mocks
        );
    }

    [Fact]
    public void Constructor_InitializesDefaultSpeedValues()
    {
        // Arrange (in CreateViewModel)
        
        // Act
        _viewModel = CreateViewModel();

        // Assert
        _viewModel.DownloadSpeed.Should().Be("0 B/s");
    }

    public void Dispose()
    {
        _viewModel?.Dispose();
    }
}
```

### Assertion Library

Use `AwesomeAssertions` (FluentAssertions-style):

```csharp
result.Should().Be(expected);
result.Should().BeGreaterThan(0);
collection.Should().HaveCount(3);
action.Should().Throw<InvalidOperationException>();
```

## Error Handling

### Standard Try-Catch Pattern

```csharp
public async Task<AppSettings> LoadSettingsAsync()
{
    try
    {
        return await _repository.GetSettingsAsync();
    }
    catch (Exception ex)
    {
        Log.Error(ex, "Failed to load settings");
        return new AppSettings(); // Return defaults
    }
}
```

### Background Service Error Isolation

```csharp
private async Task PollAsync()
{
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
}
```

## AXAML Styling Patterns

### Resource References

```xml
<!-- Use StaticResource for theme colors -->
<Border Background="{StaticResource SurfaceBackground}">
    <TextBlock 
        Text="{Binding DownloadSpeed}"
        Foreground="{StaticResource DownloadColor}"
        FontWeight="{StaticResource HeadingWeight}"/>
</Border>
```

### Control Templates

```xml
<!-- Custom control with bindable properties -->
<controls:CircularGauge 
    Value="{Binding CpuUsagePercent}"
    StrokeColor="{StaticResource CpuColor}"
    Size="80"/>
```

### Accessibility

```xml
<!-- Always add AutomationProperties -->
<Button 
    Command="{Binding RefreshCommand}"
    AutomationProperties.Name="Refresh network statistics"
    AutomationProperties.HelpText="Click to refresh the current network statistics">
    ⟳
</Button>
```

## DateTime Usage Guidelines

This is a **local desktop monitoring application**. DateTime usage follows these intentional patterns:

### Use `DateTime.Now` (Local Time)

| Scenario | Rationale |
| -------- | --------- |
| Hourly/daily aggregations | User's "today" should match their local calendar day |
| Speed snapshot timestamps | Displayed in local time to user |
| UI date pickers and filters | Users work in local time |
| Data retention cleanup | Cutoffs based on local day boundaries |
| LastUpdated fields | Shown to user in local time |

### Use `DateTime.UtcNow`

| Scenario | Rationale |
| -------- | --------- |
| Cache TTL checks | Internal timing, not displayed to user |
| Cross-timezone comparisons | N/A for desktop app |
| Distributed timestamps | N/A for desktop app |

### Example

```csharp
// ✅ Correct: Aggregations use local time (user's day/hour)
var now = DateTime.Now;
var currentHour = new DateTime(now.Year, now.Month, now.Day, now.Hour, 0, 0);
var today = DateOnly.FromDateTime(now);

// ✅ Correct: Cache TTL uses UTC (internal, not displayed)
if (DateTime.UtcNow - entry.CachedAt < CacheTtl)
    return cached;
```

### Where to See It
- `src/WireBound.Avalonia/Services/DataPersistenceService.cs` - Local time for aggregations
- `src/WireBound.Avalonia/Services/SystemHistoryService.cs` - Local time for aggregations  
- `src/WireBound.Avalonia/Services/DnsResolverService.cs` - UTC for cache TTL

---

## Commit Conventions

Use conventional commits:

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
