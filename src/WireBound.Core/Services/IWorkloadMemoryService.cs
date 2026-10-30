using WireBound.Platform.Abstract.Models;

namespace WireBound.Core.Services;

public interface IWorkloadMemoryService
{
    Task<IReadOnlyList<MemoryWorkload>> CaptureAsync(CancellationToken cancellationToken);
    Task<IReadOnlyList<MemoryWorkload>> GetContainerProcessesAsync(string containerId, CancellationToken cancellationToken);
}
