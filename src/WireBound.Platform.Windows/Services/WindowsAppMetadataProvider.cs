using System.Collections.Concurrent;
using System.Diagnostics;
using System.Runtime.Versioning;
using Microsoft.Extensions.Logging;
using WireBound.Platform.Abstract.Services;

namespace WireBound.Platform.Windows.Services;

/// <summary>
/// Windows implementation of IAppMetadataProvider.
/// Uses FileVersionInfo for publisher extraction and Process API for parent process lookup.
/// </summary>
[SupportedOSPlatform("windows")]
public sealed class WindowsAppMetadataProvider : IAppMetadataProvider
{
    private readonly ILogger<WindowsAppMetadataProvider>? _logger;
    private readonly ConcurrentDictionary<string, string?> _publisherCache = new(StringComparer.OrdinalIgnoreCase);

    public WindowsAppMetadataProvider(ILogger<WindowsAppMetadataProvider>? logger = null)
    {
        _logger = logger;
    }

    public string? GetPublisher(string executablePath)
    {
        if (string.IsNullOrWhiteSpace(executablePath))
            return null;

        return _publisherCache.GetOrAdd(executablePath, static path =>
        {
            try
            {
                var versionInfo = FileVersionInfo.GetVersionInfo(path);
                if (!string.IsNullOrWhiteSpace(versionInfo.CompanyName))
                    return versionInfo.CompanyName;
            }
            catch
            {
                // File may not exist, be locked, or not be a valid PE
            }

            return null;
        });
    }

    public string? GetCategoryFromOsMetadata(string executableName) => null;

    public string? GetParentProcessName(int processId)
    {
        if (processId <= 0)
            return null;

        try
        {
            using var process = Process.GetProcessById(processId);
            // On Windows, get parent process ID via P/Invoke or WMI is complex.
            // Use the simpler approach: NtQueryInformationProcess or ProcessBasicInformation.
            // For now, use the managed approach via the process start info.
            var parentId = GetParentProcessId(processId);
            if (parentId <= 0)
                return null;

            using var parent = Process.GetProcessById(parentId);
            return parent.ProcessName;
        }
        catch
        {
            // Process may have exited or access denied
            return null;
        }
    }

    public Task InitializeAsync(CancellationToken cancellationToken = default)
    {
        _logger?.LogDebug("Windows app metadata provider initialized");
        return Task.CompletedTask;
    }

    private static int GetParentProcessId(int processId)
    {
        try
        {
            // Use the Windows Management Instrumentation to get parent PID
            // This is simpler and more reliable than P/Invoke for our use case
            using var process = Process.GetProcessById(processId);

            // .NET 9+ exposes a way to get the parent process handle indirectly.
            // For compatibility, we use the /proc-style approach via performance counters
            // or fall back to the simpler WMI approach.
            // Since this is called infrequently (only for "Other" processes), WMI overhead is acceptable.

            // Use the Windows Toolhelp32 snapshot approach via P/Invoke
            return GetParentPidFromToolhelp(processId);
        }
        catch
        {
            return -1;
        }
    }

    private static int GetParentPidFromToolhelp(int processId)
    {
        const uint TH32CS_SNAPPROCESS = 0x00000002;

        nint snapshot = CreateToolhelp32Snapshot(TH32CS_SNAPPROCESS, 0);
        if (snapshot == nint.Zero || snapshot == -1)
            return -1;

        try
        {
            var entry = new PROCESSENTRY32
            {
                dwSize = (uint)System.Runtime.InteropServices.Marshal.SizeOf<PROCESSENTRY32>()
            };

            if (!Process32First(snapshot, ref entry))
                return -1;

            do
            {
                if (entry.th32ProcessID == processId)
                    return (int)entry.th32ParentProcessID;
            }
            while (Process32Next(snapshot, ref entry));

            return -1;
        }
        finally
        {
            CloseHandle(snapshot);
        }
    }

    [System.Runtime.InteropServices.DllImport("kernel32.dll", SetLastError = true)]
    private static extern nint CreateToolhelp32Snapshot(uint dwFlags, uint th32ProcessID);

    [System.Runtime.InteropServices.DllImport("kernel32.dll", SetLastError = true)]
    private static extern bool Process32First(nint hSnapshot, ref PROCESSENTRY32 lppe);

    [System.Runtime.InteropServices.DllImport("kernel32.dll", SetLastError = true)]
    private static extern bool Process32Next(nint hSnapshot, ref PROCESSENTRY32 lppe);

    [System.Runtime.InteropServices.DllImport("kernel32.dll", SetLastError = true)]
    private static extern bool CloseHandle(nint hObject);

    [System.Runtime.InteropServices.StructLayout(System.Runtime.InteropServices.LayoutKind.Sequential, CharSet = System.Runtime.InteropServices.CharSet.Auto)]
    private struct PROCESSENTRY32
    {
        public uint dwSize;
        public uint cntUsage;
        public uint th32ProcessID;
        public nint th32DefaultHeapID;
        public uint th32ModuleID;
        public uint cntThreads;
        public uint th32ParentProcessID;
        public int pcPriClassBase;
        public uint dwFlags;
        [System.Runtime.InteropServices.MarshalAs(System.Runtime.InteropServices.UnmanagedType.ByValTStr, SizeConst = 260)]
        public string szExeFile;
    }
}
