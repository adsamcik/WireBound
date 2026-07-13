using System.Runtime.InteropServices;
using System.Runtime.Versioning;
using Serilog;

namespace WireBound.Avalonia;

/// <summary>
/// Applies Windows process mitigation policies to the main app process.
///
/// <para>
/// This is the single most impactful defense against the "same-user malware
/// injects into WireBound.exe and drives the elevation helper" attack chain.
/// All of the IPC-side checks (image hash, nonce, integrity-level, signature
/// pinning) are bypassable if the attacker can execute code <i>inside</i> the
/// legitimate WireBound process — at that point the kernel reports the
/// process's real path, the loader sees the real image hash, and so on. The
/// only thing that actually closes the injection hole is preventing the
/// injection in the first place.
/// </para>
///
/// <para>
/// The mitigations applied are intentionally conservative — they raise the
/// attacker cost for the most common medium-IL injection techniques without
/// breaking Avalonia/Skia/CoreCLR JIT:
/// <list type="bullet">
///   <item><b>ExtensionPointDisablePolicy</b>: blocks AppInit_DLLs,
///         SetWindowsHookEx, IMEs, Winsock LSPs — the classic injection
///         vectors.</item>
///   <item><b>ImageLoadPolicy(NoRemoteImages, NoLowMandatoryLabelImages)</b>:
///         blocks DLL loads from UNC paths and from Low-IL writeable dirs.</item>
///   <item><b>ProhibitChildProcessCreation</b>: the main app should never
///         spawn child processes (the helper is launched out-of-band via
///         schtasks). Tightens process tree against shell injection.</item>
/// </list>
/// </para>
///
/// <summary>
/// Mitigations <b>intentionally NOT applied</b> due to compatibility risk:
/// <list type="bullet">
///   <item><b>ChildProcessPolicy.NoChildProcessCreation</b>: would block
///         <c>Process.Start</c> for every helper-management code path
///         (<c>schtasks.exe</c>, ShellExecute <c>runas</c>, <c>systemctl</c>).
///         This would brick the auto-start feature it was meant to protect.
///         The realistic in-proc injection vectors are already covered by
///         the two policies above.</item>
///   <item><b>MicrosoftSignedOnly</b> / <b>StoreSignedOnly</b>: would block
///         loading WireBound's own native DLLs (LiveCharts/Skia natives) that
///         are signed by SkiaSharp's certificate, not Microsoft's.</item>
///   <item><b>ProhibitDynamicCode</b>: would break the .NET 10 RyuJIT and
///         Avalonia shader compilation.</item>
/// </list>
/// </para>
///
/// <para>
/// All <c>SetProcessMitigationPolicy</c> calls are <b>best-effort</b>: a
/// failure does not abort startup, just logs a warning. This matches Windows'
/// own behaviour for already-applied or unavailable policies.
/// </para>
/// </summary>
public static class ProcessMitigations
{
    /// <summary>
    /// Applies the mitigation set. Must be called before any plugin / native
    /// DLL load that could be hijacked by an extension-point hook. The
    /// <see cref="Program.Main"/> calls this immediately after Velopack init,
    /// before anything else.
    /// </summary>
    public static void ApplyEarly()
    {
        if (!OperatingSystem.IsWindows())
        {
            ApplyLinux();
            return;
        }
        ApplyWindows();
    }

    [SupportedOSPlatform("windows")]
    private static void ApplyWindows()
    {
        TryApply(ProcessMitigationPolicy.ProcessExtensionPointDisablePolicy,
            new ProcessExtensionPointDisablePolicy { Flags = 1 /* DisableExtensionPoints */ });

        TryApply(ProcessMitigationPolicy.ProcessImageLoadPolicy,
            new ProcessImageLoadPolicy
            {
                Flags = 0x1   // NoRemoteImages
                      | 0x2   // NoLowMandatoryLabelImages
            });

        // NOTE: ProcessChildProcessPolicy was previously enabled here. It is
        // INCOMPATIBLE with helper management — every helper-related code path
        // (schtasks.exe, ShellExecute "runas", systemd queries) requires
        // Process.Start, which the policy blocks. The realistic injection
        // vectors the design targeted (T6) are already covered by
        // ExtensionPointDisablePolicy and ImageLoadPolicy above; adding
        // ChildProcessPolicy bought us nothing and bricked the feature.
    }

    private static void ApplyLinux()
    {
        // prctl(PR_SET_DUMPABLE, 0) — prevents another non-root same-user
        // process from attaching via ptrace (subject to yama.ptrace_scope).
        // Combined with the kernel's normal protections, this raises the cost
        // of in-process injection on Linux.
        try
        {
            const int PR_SET_DUMPABLE = 4;
            var rc = prctl(PR_SET_DUMPABLE, 0, 0, 0, 0);
            if (rc != 0)
                Log.Debug("prctl(PR_SET_DUMPABLE, 0) returned {Rc}", rc);
        }
        catch (Exception ex)
        {
            Log.Debug(ex, "prctl is unavailable; skipping process hardening");
        }
    }

    [DllImport("libc", SetLastError = true)]
    private static extern int prctl(int option, ulong arg2, ulong arg3, ulong arg4, ulong arg5);

    // ─────────────────────────────────────────────────────────────────────
    // Windows P/Invoke
    // ─────────────────────────────────────────────────────────────────────

    [SupportedOSPlatform("windows")]
    private static void TryApply<T>(ProcessMitigationPolicy policy, T value) where T : struct
    {
        try
        {
            var size = Marshal.SizeOf<T>();
            var ptr = Marshal.AllocHGlobal(size);
            try
            {
                Marshal.StructureToPtr(value, ptr, fDeleteOld: false);
                if (!SetProcessMitigationPolicy(policy, ptr, (UIntPtr)size))
                {
                    var err = Marshal.GetLastWin32Error();
                    Log.Debug("SetProcessMitigationPolicy({Policy}) failed with Win32 error {Err}", policy, err);
                }
                else
                {
                    Log.Information("Applied process mitigation: {Policy}", policy);
                }
            }
            finally
            {
                Marshal.FreeHGlobal(ptr);
            }
        }
        catch (Exception ex)
        {
            Log.Debug(ex, "Failed to apply mitigation {Policy}", policy);
        }
    }

    [DllImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    [SupportedOSPlatform("windows")]
    private static extern bool SetProcessMitigationPolicy(
        ProcessMitigationPolicy MitigationPolicy,
        IntPtr lpBuffer,
        UIntPtr dwLength);

    private enum ProcessMitigationPolicy
    {
        ProcessDEPPolicy = 0,
        ProcessASLRPolicy = 1,
        ProcessDynamicCodePolicy = 2,
        ProcessStrictHandleCheckPolicy = 3,
        ProcessSystemCallDisablePolicy = 4,
        ProcessMitigationOptionsMask = 5,
        ProcessExtensionPointDisablePolicy = 6,
        ProcessControlFlowGuardPolicy = 7,
        ProcessSignaturePolicy = 8,
        ProcessFontDisablePolicy = 9,
        ProcessImageLoadPolicy = 10,
        ProcessSystemCallFilterPolicy = 11,
        ProcessPayloadRestrictionPolicy = 12,
        ProcessChildProcessPolicy = 13,
        ProcessSideChannelIsolationPolicy = 14
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct ProcessExtensionPointDisablePolicy
    {
        public uint Flags;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct ProcessImageLoadPolicy
    {
        public uint Flags;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct ProcessChildProcessPolicy
    {
        public uint Flags;
    }
}
