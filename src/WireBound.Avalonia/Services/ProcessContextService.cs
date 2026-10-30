namespace WireBound.Avalonia.Services;

public readonly record struct ProcessInstanceId(int Pid, long StartMarker);

/// <summary>Navigation context for exact app instances or likely workload hosts. Names never become process filters.</summary>
public sealed record ProcessContext(
    string WorkloadId,
    string Title,
    string Evidence,
    DateTime CapturedAt,
    IReadOnlySet<ProcessInstanceId> LikelyInstances,
    IReadOnlySet<ProcessInstanceId>? ConfirmedInstances = null);

public sealed class ProcessContextService
{
    public ProcessContext? Current { get; private set; }
    public event Action? Changed;

    public void Set(ProcessContext context)
    {
        Current = context;
        Changed?.Invoke();
    }

    public void Clear()
    {
        if (Current is null) return;
        Current = null;
        Changed?.Invoke();
    }
}
