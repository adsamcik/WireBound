using System.Runtime.InteropServices;
using System.Runtime.Versioning;
using System.Text;
using WireBound.Platform.Abstract.Models;

namespace WireBound.Platform.Windows.Services;

/// <summary>Image paths for protected processes, validated against a later process-generation snapshot by the caller.</summary>
[SupportedOSPlatform("windows")]
internal static class WindowsProcessImagePath
{
    [StructLayout(LayoutKind.Sequential)]
    private struct UnicodeString
    {
        public ushort Length, MaximumLength;
        public nint Buffer;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct ProcessIdInformation
    {
        public nint ProcessId;
        public UnicodeString ImageName;
    }

    [DllImport("ntdll.dll")]
    private static extern int NtQuerySystemInformation(int kind, ref ProcessIdInformation information, int size, out int required);

    [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern uint QueryDosDeviceW(string name, StringBuilder target, int capacity);

    internal static IReadOnlyDictionary<int, string> Capture(IReadOnlyList<ProcessResourceData> samples, CancellationToken cancellationToken)
    {
        var results = new Dictionary<int, string>();
        var candidates = samples.Where(p => p.StartMarker != 0 && p.ProcessId > 4 && p.ExecutablePath.Length == 0).ToArray();
        if (candidates.Length == 0 || !OperatingSystem.IsWindowsVersionAtLeast(10)) return results;
        var drives = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (var drive in Environment.GetLogicalDrives())
        {
            var target = new StringBuilder(4096);
            var letter = drive.TrimEnd('\\');
            if (QueryDosDeviceW(letter, target, target.Capacity) > 0) drives[target.ToString()] = letter;
        }
        const int capacity = 65534; // UNICODE_STRING lengths are unsigned 16-bit byte counts.
        var buffer = Marshal.AllocHGlobal(capacity);
        try
        {
            foreach (var sample in candidates)
            {
                cancellationToken.ThrowIfCancellationRequested();
                var information = new ProcessIdInformation
                {
                    ProcessId = sample.ProcessId,
                    ImageName = new UnicodeString { Buffer = buffer, MaximumLength = capacity }
                };
                // SYSTEM_PROCESS_ID_INFORMATION: winsiderss/phnt/ntexapi.h (class 88).
                var status = NtQuerySystemInformation(88, ref information, Marshal.SizeOf<ProcessIdInformation>(), out _);
                var image = information.ImageName;
                if (status < 0 || image.Buffer != buffer || image.Length == 0 || image.Length % 2 != 0 || image.Length > capacity) continue;
                var nativePath = Marshal.PtrToStringUni(buffer, image.Length / 2);
                var path = nativePath is null ? null : ToDosPath(nativePath, drives);
                if (path is not null) results[sample.ProcessId] = path;
            }
        }
        finally { Marshal.FreeHGlobal(buffer); }
        return results;
    }

    internal static string? ToDosPath(string nativePath, IReadOnlyDictionary<string, string> drives)
    {
        foreach (var (device, drive) in drives.OrderByDescending(p => p.Key.Length))
            if (nativePath.StartsWith(device + "\\", StringComparison.OrdinalIgnoreCase))
                return drive + nativePath[device.Length..];
        return null;
    }
}
