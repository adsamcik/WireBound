# WireBound Development Instructions

## Project Overview
WireBound is a Windows network traffic monitoring application built with .NET 10 and MAUI.

## Architecture
- **MVVM Pattern**: Using CommunityToolkit.Mvvm for ViewModels
- **Dependency Injection**: Microsoft.Extensions.DependencyInjection
- **Background Services**: Manually started background polling service
- **Database**: SQLite with Entity Framework Core
- **Charts**: LiveChartsCore.SkiaSharpView.Maui for real-time visualization
- **Navigation**: Shell-based flyout navigation

## Key Components

### Services
- `INetworkMonitorService` - Polls network statistics
- `IDataPersistenceService` - Saves data to SQLite
- `NetworkPollingBackgroundService` - Background timer service (started in App.xaml.cs)

### Models
- `NetworkStats` - Real-time speed and usage data
- `NetworkAdapter` - Network interface information
- `DailyUsage` / `HourlyUsage` - Historical aggregated data
- `AppSettings` - User preferences

### ViewModels
- `MainViewModel` - Navigation control
- `DashboardViewModel` - Real-time stats, charts, time range selection, statistics
- `HistoryViewModel` - Historical data visualization
- `SettingsViewModel` - App configuration

### Pages
- `DashboardPage` - Real-time monitoring with interactive chart
- `HistoryPage` - Historical usage data
- `SettingsPage` - App configuration

## Development Guidelines

### Adding New Features
1. Create models in `/Models`
2. Add service interfaces in `/Services`
3. Register services in `MauiProgram.cs`
4. Create ViewModels with `ObservableObject` base
5. Create Pages in `/Views`
6. Add navigation in `AppShell.xaml`

### Database Changes
1. Modify `WireBoundDbContext`
2. Add migrations with EF Core tools
3. Ensure `EnsureCreated()` handles new tables

### Styling
- Colors defined in `/Resources/Styles/Colors.xaml`
- Styles defined in `/Resources/Styles/Styles.xaml`
- Use `{StaticResource}` for consistency

## Build Commands
```powershell
dotnet restore
dotnet build
dotnet run --project src/WireBound/WireBound.csproj
```

## Testing Network Accuracy
Download a known file size and compare with app readings.
If inaccurate, toggle IP Helper API mode in settings.

## Workflow
- Commit changes after completing each task or feature effort
