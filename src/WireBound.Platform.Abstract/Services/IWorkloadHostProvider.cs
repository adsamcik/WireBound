using WireBound.Platform.Abstract.Models;

namespace WireBound.Platform.Abstract.Services;

public interface IWorkloadHostProvider
{
    WorkloadHost? IdentifyHost(string processName, string executablePath);

    /// <summary>Read named guest observations without starting or entering guests.</summary>
    Task<IReadOnlyList<MemoryWorkload>> GetVirtualMachinesAsync(CancellationToken cancellationToken = default);
}
