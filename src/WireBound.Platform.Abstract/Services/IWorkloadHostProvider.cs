using WireBound.Platform.Abstract.Models;

namespace WireBound.Platform.Abstract.Services;

public interface IWorkloadHostProvider
{
    WorkloadHost? IdentifyHost(string processName, string executablePath);
}
