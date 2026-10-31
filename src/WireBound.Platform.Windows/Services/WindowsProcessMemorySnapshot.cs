using System.Buffers.Binary;
using System.Runtime.InteropServices;
using System.Runtime.Versioning;
using System.Text;
using WireBound.Platform.Abstract.Models;

namespace WireBound.Platform.Windows.Services;

/// <summary>System-wide resident readings, including processes whose handles are protected.</summary>
[SupportedOSPlatform("windows")]
internal static class WindowsProcessMemorySnapshot
{
    [DllImport("ntdll.dll")]
    private static extern int NtQuerySystemInformation(int informationClass, nint buffer, int length, out int required);

    internal static IReadOnlyList<ProcessResourceData>? Capture(bool includePrivate, CancellationToken cancellationToken)
    {
        // SYSTEM_PROCESS_INFORMATION (Windows 10+). Fail back to the documented
        // per-process APIs if the native query or its validated layout is unavailable.
        // Layout reference: github.com/winsiderss/phnt/blob/master/ntexapi.h
        if (!OperatingSystem.IsWindowsVersionAtLeast(10)) return null;
        var capacity = 1024 * 1024;
        for (var attempt = 0; attempt < 5 && capacity <= 64 * 1024 * 1024; attempt++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var buffer = Marshal.AllocHGlobal(capacity);
            try
            {
                var status = NtQuerySystemInformation(5, buffer, capacity, out var required);
                if (status == unchecked((int)0xC0000004) || status == unchecked((int)0xC0000023))
                {
                    if (required < 0 || required > 64 * 1024 * 1024 - 65536) return null;
                    capacity = Math.Max(capacity * 2, required + 65536);
                    continue;
                }
                if (status < 0 || required <= 0 || required > capacity) return null;
                cancellationToken.ThrowIfCancellationRequested();
                var bytes = new byte[required];
                Marshal.Copy(buffer, bytes, 0, required);
                return Parse(bytes, (ulong)(nuint)buffer, IntPtr.Size, includePrivate);
            }
            finally { Marshal.FreeHGlobal(buffer); }
        }
        return null;
    }

    internal static IReadOnlyList<ProcessResourceData>? Parse(ReadOnlySpan<byte> data, ulong bufferAddress, int pointerSize, bool includePrivate)
    {
        if (pointerSize is not (4 or 8)) return null;
        var headerSize = pointerSize == 8 ? 256 : 184;
        var pidOffset = pointerSize == 8 ? 80 : 68;
        var wsOffset = pointerSize == 8 ? 144 : 104;
        var commitOffset = pointerSize == 8 ? 200 : 132;
        var namePointerOffset = pointerSize == 8 ? 64 : 60;
        var rows = new List<ProcessResourceData>();
        var ids = new HashSet<int>();
        var offset = 0;
        while (offset <= data.Length - headerSize)
        {
            var entry = data[offset..];
            var next = BinaryPrimitives.ReadUInt32LittleEndian(entry);
            if (next > int.MaxValue || next != 0 && (next < headerSize || next > entry.Length)) return null;
            var pid = ReadPointer(entry[pidOffset..], pointerSize);
            var workingSet = ReadPointer(entry[wsOffset..], pointerSize);
            var commit = ReadPointer(entry[commitOffset..], pointerSize);
            var privateSet = BinaryPrimitives.ReadUInt64LittleEndian(entry[8..]);
            var created = BinaryPrimitives.ReadInt64LittleEndian(entry[32..]);
            var userTime = BinaryPrimitives.ReadInt64LittleEndian(entry[40..]);
            var kernelTime = BinaryPrimitives.ReadInt64LittleEndian(entry[48..]);
            if (pid > int.MaxValue || workingSet > long.MaxValue || commit > long.MaxValue || privateSet > workingSet
                || userTime < 0 || kernelTime < 0 || userTime > long.MaxValue - kernelTime) return null;
            long startMarker;
            try { startMarker = created == 0 ? 0 : DateTime.FromFileTimeUtc(created).Ticks; }
            catch (ArgumentOutOfRangeException) { return null; }
            var nameLength = BinaryPrimitives.ReadUInt16LittleEndian(entry[56..]);
            var namePointer = ReadPointer(entry[namePointerOffset..], pointerSize);
            var name = pid == 4 ? "System" : $"Process {pid}";
            if (nameLength > 0)
            {
                if (nameLength % 2 != 0 || nameLength > data.Length || namePointer < bufferAddress
                    || namePointer - bufferAddress > (ulong)(data.Length - nameLength)) return null;
                name = Encoding.Unicode.GetString(data.Slice((int)(namePointer - bufferAddress), nameLength));
                if (name.EndsWith(".exe", StringComparison.OrdinalIgnoreCase)) name = name[..^4];
            }
            if (pid != 0)
            {
                if (!ids.Add((int)pid)) return null;
                rows.Add(new ProcessResourceData
                {
                    ProcessId = (int)pid,
                    ProcessName = name,
                    StartMarker = startMarker,
                    WorkingSetBytes = (long)workingSet,
                    PrivateBytes = (long)commit,
                    PrivateWorkingSetBytes = includePrivate ? (long)privateSet : null,
                    CpuTimeTicks = userTime + kernelTime
                });
            }
            if (next == 0) return rows;
            offset += (int)next;
        }
        return null;
    }

    private static ulong ReadPointer(ReadOnlySpan<byte> value, int size) => size == 8
        ? BinaryPrimitives.ReadUInt64LittleEndian(value) : BinaryPrimitives.ReadUInt32LittleEndian(value);
}
