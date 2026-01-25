using WireBound.Core.Models;
using WireBound.Platform.Abstract.Models;

namespace WireBound.Core.Services;

/// <summary>
/// Unified service for persisting network data to database.
/// Implements the Interface Segregation Principle by composing focused repository interfaces.
/// </summary>
/// <remarks>
/// For new code, prefer injecting the specific interfaces:
/// <list type="bullet">
/// <item><see cref="INetworkUsageRepository"/> for network usage data</item>
/// <item><see cref="IAppUsageRepository"/> for per-application tracking</item>
/// <item><see cref="ISettingsRepository"/> for application settings</item>
/// <item><see cref="ISpeedSnapshotRepository"/> for chart history data</item>
/// </list>
/// </remarks>
public interface IDataPersistenceService : 
    INetworkUsageRepository, 
    IAppUsageRepository, 
    ISettingsRepository, 
    ISpeedSnapshotRepository
{
    // All members are inherited from the composed interfaces.
    // This interface exists for backward compatibility and as a convenience
    // when a component needs access to multiple repository capabilities.
}
