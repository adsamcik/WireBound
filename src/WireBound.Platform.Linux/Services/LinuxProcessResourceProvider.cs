using System.Globalization;
using System.Runtime.Versioning;
using WireBound.Platform.Abstract.Models;
using WireBound.Platform.Abstract.Services;

namespace WireBound.Platform.Linux.Services;

/// <summary>
/// Linux implementation of per-process resource data provider.
/// Reads from /proc filesystem for CPU times and memory stats.
/// </summary>
[SupportedOSPlatform("linux")]
public sealed class LinuxProcessResourceProvider : IProcessResourceProvider
{
    private static readonly long TicksPerClockTick = TimeSpan.TicksPerSecond / GetClockTicksPerSecond();

    public Task<IReadOnlyList<ProcessResourceData>> GetProcessResourceDataAsync(
        CancellationToken cancellationToken = default)
    {
        var results = new List<ProcessResourceData>();
        var pageSize = Environment.SystemPageSize;

        string[] procDirs;
        try
        {
            procDirs = Directory.GetDirectories("/proc");
        }
        catch
        {
            return Task.FromResult<IReadOnlyList<ProcessResourceData>>(results);
        }

        foreach (var procDir in procDirs)
        {
            cancellationToken.ThrowIfCancellationRequested();

            var dirName = Path.GetFileName(procDir);
            if (!int.TryParse(dirName, out var pid))
                continue;

            try
            {
                var (processName, cpuTimeTicks) = ReadStat(procDir, pid);
                var (rssBytes, privateBytes) = ReadStatm(procDir, pageSize);
                var exePath = ReadExePath(procDir);

                results.Add(new ProcessResourceData
                {
                    ProcessId = pid,
                    ProcessName = processName,
                    ExecutablePath = exePath,
                    PrivateBytes = privateBytes,
                    WorkingSetBytes = rssBytes,
                    CpuTimeTicks = cpuTimeTicks
                });
            }
            catch
            {
                // Process may have exited or be inaccessible — skip
            }
        }

        return Task.FromResult<IReadOnlyList<ProcessResourceData>>(results);
    }

    /// <summary>
    /// Read /proc/[pid]/stat for process name and CPU time (utime + stime).
    /// </summary>
    private static (string name, long cpuTimeTicks) ReadStat(string procDir, int pid)
    {
        var statPath = Path.Combine(procDir, "stat");
        var content = File.ReadAllText(statPath);

        // Format: pid (comm) state ... utime stime ...
        // comm can contain spaces/parens, so find the last ')' to delimit it
        var openParen = content.IndexOf('(');
        var closeParen = content.LastIndexOf(')');

        var name = content.Substring(openParen + 1, closeParen - openParen - 1);

        // Fields after ')': state(0) ppid(1) ... utime(11) stime(12)
        var fields = content[(closeParen + 2)..].Split(' ', StringSplitOptions.RemoveEmptyEntries);

        var utime = long.Parse(fields[11], CultureInfo.InvariantCulture);
        var stime = long.Parse(fields[12], CultureInfo.InvariantCulture);

        // Convert clock ticks to 100-ns ticks (same unit as Windows TotalProcessorTime)
        var cpuTimeTicks = (utime + stime) * TicksPerClockTick;

        return (name, cpuTimeTicks);
    }

    /// <summary>
    /// Read /proc/[pid]/statm for memory: RSS and private (RSS - shared) pages.
    /// </summary>
    private static (long rssBytes, long privateBytes) ReadStatm(string procDir, int pageSize)
    {
        var statmPath = Path.Combine(procDir, "statm");
        var content = File.ReadAllText(statmPath);
        var fields = content.Split(' ', StringSplitOptions.RemoveEmptyEntries);

        // Fields: size resident shared text lib data dt
        var rssPages = long.Parse(fields[1], CultureInfo.InvariantCulture);
        var sharedPages = long.Parse(fields[2], CultureInfo.InvariantCulture);

        var rssBytes = rssPages * pageSize;
        var privateBytes = Math.Max(0, (rssPages - sharedPages) * pageSize);

        return (rssBytes, privateBytes);
    }

    /// <summary>
    /// Read /proc/[pid]/exe symlink for the executable path.
    /// </summary>
    private static string ReadExePath(string procDir)
    {
        try
        {
            var exeLink = Path.Combine(procDir, "exe");
            var info = new FileInfo(exeLink);
            return info.LinkTarget ?? string.Empty;
        }
        catch
        {
            return string.Empty;
        }
    }

    private static long GetClockTicksPerSecond()
    {
        // sysconf(_SC_CLK_TCK) is typically 100 on Linux
        return 100;
    }
}
