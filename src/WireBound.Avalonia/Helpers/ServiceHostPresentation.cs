using WireBound.Platform.Abstract.Models;

namespace WireBound.Avalonia.Helpers;

internal static class ServiceHostPresentation
{
    public static bool HasHostName(string name) => name.Equals("svchost", StringComparison.OrdinalIgnoreCase)
        || name.Equals("svchost.exe", StringComparison.OrdinalIgnoreCase);

    public static string Name(ServiceHostInfo host) => host.Services.Count == 0 ? "Service host"
        : (host.IsSharedHost ? "Service host: " : "") + ServiceName(host.Services[0]) + (host.Services.Count > 1 ? $" +{host.Services.Count - 1}" : "");

    public static string SearchTerms(ServiceHostInfo host) => (host.IsSharedHost ? "svchost " : "")
        + string.Join(" ", host.Services.Select(s => $"{s.Name} {s.DisplayName}"));

    public static string Details(ServiceHostInfo host) => (host.Services.Count == 0
        ? "Service names unavailable in this sample. Refresh to retry."
        : "Services reported by Windows:\n" + string.Join("\n", host.Services.Select(s => $"{ServiceName(s)} ({s.Name})")))
        + (host.IsSharedHost || host.Services.Count > 1
            ? "\nRAM and CPU are measured for the whole host. Usage cannot be split between its services."
            : "\nRAM and CPU are measured for this service process.");

    private static string ServiceName(HostedServiceInfo service) => string.IsNullOrWhiteSpace(service.DisplayName) ? service.Name : service.DisplayName;
}
