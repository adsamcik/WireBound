using System.Buffers.Binary;
using System.Diagnostics;
using System.Runtime.Versioning;
using System.Text;
using WireBound.Platform.Windows.Services;

namespace WireBound.Tests.Platform;

[SupportedOSPlatform("windows")]
public class WindowsProcessMemorySnapshotTests
{
    private const ulong Address = 0x100000;
    private static readonly DateTime Created = new(2026, 10, 31, 0, 0, 0, DateTimeKind.Utc);

    private static byte[] Snapshot(int pointerSize)
    {
        var header = pointerSize == 8 ? 256 : 184;
        var name = Encoding.Unicode.GetBytes("example.exe");
        var bytes = new byte[header + name.Length];
        BinaryPrimitives.WriteUInt64LittleEndian(bytes.AsSpan(8), 500);
        BinaryPrimitives.WriteInt64LittleEndian(bytes.AsSpan(32), Created.ToFileTimeUtc());
        BinaryPrimitives.WriteInt64LittleEndian(bytes.AsSpan(40), 100);
        BinaryPrimitives.WriteInt64LittleEndian(bytes.AsSpan(48), 200);
        BinaryPrimitives.WriteUInt16LittleEndian(bytes.AsSpan(56), (ushort)name.Length);
        void Pointer(int offset, ulong value)
        {
            if (pointerSize == 8) BinaryPrimitives.WriteUInt64LittleEndian(bytes.AsSpan(offset), value);
            else BinaryPrimitives.WriteUInt32LittleEndian(bytes.AsSpan(offset), (uint)value);
        }
        Pointer(pointerSize == 8 ? 64 : 60, Address + (uint)header);
        Pointer(pointerSize == 8 ? 80 : 68, 42);
        Pointer(pointerSize == 8 ? 144 : 104, 1000);
        Pointer(pointerSize == 8 ? 200 : 132, 9000);
        name.CopyTo(bytes, header);
        return bytes;
    }

    [Test]
    [Arguments(4)]
    [Arguments(8)]
    public void Snapshot_UsesResidentPrivateBytes_AndRetainsExactProcessGeneration(int pointerSize)
    {
        var bytes = Snapshot(pointerSize);
        var row = WindowsProcessMemorySnapshot.Parse(bytes, Address, pointerSize, true)!.Single();
        row.ProcessId.Should().Be(42);
        row.ProcessName.Should().Be("example");
        row.StartMarker.Should().Be(Created.Ticks);
        row.PrivateWorkingSetBytes.Should().Be(500);
        row.WorkingSetBytes.Should().Be(1000);
        row.PrivateBytes.Should().Be(9000);
        row.CpuTimeTicks.Should().Be(300);
        WindowsProcessMemorySnapshot.Parse(bytes, Address, pointerSize, false)!.Single().PrivateWorkingSetBytes.Should().BeNull();
    }

    [Test]
    [Arguments("short-offset")]
    [Arguments("large-offset")]
    [Arguments("name-pointer")]
    [Arguments("private-larger-than-resident")]
    public void MalformedNativeData_IsRejectedForFallback(string failure)
    {
        var bytes = Snapshot(8);
        switch (failure)
        {
            case "short-offset": BinaryPrimitives.WriteUInt32LittleEndian(bytes, 8); break;
            case "large-offset": BinaryPrimitives.WriteUInt32LittleEndian(bytes, (uint)bytes.Length + 8); break;
            case "name-pointer": BinaryPrimitives.WriteUInt64LittleEndian(bytes.AsSpan(64), Address - 2); break;
            default: BinaryPrimitives.WriteUInt64LittleEndian(bytes.AsSpan(8), 1001); break;
        }
        WindowsProcessMemorySnapshot.Parse(bytes, Address, 8, true).Should().BeNull();
    }

    [Test]
    public async Task NativeSnapshot_ContainsCurrentProcessWithMatchingGeneration()
    {
        if (!OperatingSystem.IsWindows()) return;
        var native = WindowsProcessMemorySnapshot.Capture(true, default);
        native.Should().NotBeNull("the supported Windows native layout must work without relying on per-process fallback");
        var rows = await new WindowsProcessResourceProvider().GetProcessResourceDataAsync(default, true);
        using var current = Process.GetCurrentProcess();
        var row = rows.Single(p => p.ProcessId == current.Id);
        native!.Single(p => p.ProcessId == current.Id).StartMarker.Should().Be(row.StartMarker);
        row.StartMarker.Should().Be(current.StartTime.ToUniversalTime().Ticks);
        row.PrivateWorkingSetBytes.Should().NotBeNull();
        row.PrivateWorkingSetBytes.Should().BeLessThanOrEqualTo(row.WorkingSetBytes);
        rows.Select(p => p.ProcessId).Should().OnlyHaveUniqueItems();
    }
}
