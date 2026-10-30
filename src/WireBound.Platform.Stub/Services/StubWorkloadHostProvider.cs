using WireBound.Platform.Abstract.Models;
using WireBound.Platform.Abstract.Services;

namespace WireBound.Platform.Stub.Services;

public sealed class StubWorkloadHostProvider : IWorkloadHostProvider
{
    public WorkloadHost? IdentifyHost(string processName, string executablePath) => null;
}
