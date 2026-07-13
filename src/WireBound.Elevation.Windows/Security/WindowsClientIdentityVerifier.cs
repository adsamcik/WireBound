using System.ComponentModel;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Runtime.Versioning;
using System.Security.Cryptography;
using Microsoft.Win32.SafeHandles;
using WireBound.IPC.Security;

namespace WireBound.Elevation.Windows.Security;

/// <summary>
/// Windows implementation of <see cref="IClientIdentityVerifier"/>.
///
/// <para>
/// Resolves the client's executable path via <c>QueryFullProcessImageName</c>
/// (the recommended modern API — unlike <c>GetModuleFileNameEx</c> it cannot
/// be tricked by setting the PEB's <c>LoaderEntry.FullDllName</c> via DLL
/// injection, and unlike <c>Process.MainModule</c> it works even when the
/// caller cannot enumerate the target's modules).
/// </para>
///
/// <para>
/// Verifies (in order):
/// <list type="number">
///   <item>The PID resolves to a live process and we can open it with
///         <c>PROCESS_QUERY_LIMITED_INFORMATION</c>.</item>
///   <item>The resolved image path is canonical and inside the helper's
///         install directory (<c>AppContext.BaseDirectory</c>).</item>
///   <item>The SHA-256 of the file on disk matches the client's claim
///         (constant-time compare).</item>
///   <item>The token integrity level is &gt;= Medium (rejects sandboxed
///         Low/Untrusted callers).</item>
///   <item>If a thumbprint is pinned, Authenticode validation succeeds and
///         the signing certificate matches.</item>
/// </list>
/// </para>
/// </summary>
[SupportedOSPlatform("windows")]
public sealed class WindowsClientIdentityVerifier : IClientIdentityVerifier
{
    private readonly string _expectedInstallDir;
    private readonly string _expectedMainAppPath;
    private readonly string? _pinnedSignerThumbprint;

    /// <summary>
    /// Creates the verifier.
    /// </summary>
    /// <param name="expectedInstallDir">
    /// Canonical full path of the install directory (typically
    /// <c>AppContext.BaseDirectory</c> of the helper, which lives next to
    /// the main app exe).
    /// </param>
    /// <param name="expectedMainAppPath">
    /// Canonical full path of the main app executable. The client's resolved
    /// image path must equal this (case-insensitive on Windows).
    /// </param>
    /// <param name="pinnedSignerThumbprint">
    /// Optional Authenticode certificate thumbprint (upper-case hex, no spaces).
    /// When set, signature verification is required and the signer's
    /// thumbprint must match. When null, signature verification is skipped
    /// (suitable for unsigned dev builds; signed releases SHOULD pin).
    /// </param>
    public WindowsClientIdentityVerifier(
        string expectedInstallDir,
        string expectedMainAppPath,
        string? pinnedSignerThumbprint = null)
    {
        _expectedInstallDir = Path.GetFullPath(expectedInstallDir);
        if (!_expectedInstallDir.EndsWith(Path.DirectorySeparatorChar))
            _expectedInstallDir += Path.DirectorySeparatorChar;

        _expectedMainAppPath = Path.GetFullPath(expectedMainAppPath);
        _pinnedSignerThumbprint = pinnedSignerThumbprint?
            .Replace(" ", string.Empty, StringComparison.Ordinal)
            .ToUpperInvariant();
    }

    public ClientIdentityResult Verify(int pid, ReadOnlySpan<byte> claimedImageHash)
    {
        if (pid <= 0)
            return ClientIdentityResult.Invalid("Invalid client PID");

        // Step 1: open the client process with the least privilege required.
        using var handle = OpenLimitedProcess(pid);
        if (handle is null || handle.IsInvalid)
            return ClientIdentityResult.Invalid($"Could not open client process {pid} (may have already exited)");

        // Step 2: resolve the actual on-disk image path via the kernel.
        var actualPath = QueryFullProcessImagePath(handle);
        if (string.IsNullOrEmpty(actualPath))
            return ClientIdentityResult.Invalid("Could not resolve client image path");

        var canonicalActual = Path.GetFullPath(actualPath);

        // Step 3: confine to the install directory AND match the expected main exe.
        if (!canonicalActual.StartsWith(_expectedInstallDir, StringComparison.OrdinalIgnoreCase))
            return ClientIdentityResult.Invalid(
                $"Client image '{canonicalActual}' is outside install dir '{_expectedInstallDir}'");

        if (!string.Equals(canonicalActual, _expectedMainAppPath, StringComparison.OrdinalIgnoreCase))
            return ClientIdentityResult.Invalid(
                $"Client image '{canonicalActual}' is not the expected main app '{_expectedMainAppPath}'");

        // Step 4: independently hash the file on disk and constant-time compare.
        byte[] actualHash;
        try
        {
            actualHash = ClientImageHasher.HashFile(canonicalActual);
        }
        catch (Exception ex)
        {
            return ClientIdentityResult.Invalid($"Could not hash client image: {ex.Message}");
        }

        if (claimedImageHash.Length != actualHash.Length ||
            !CryptographicOperations.FixedTimeEquals(claimedImageHash, actualHash))
        {
            return ClientIdentityResult.Invalid("Client image hash does not match disk file");
        }

        // Step 5: enforce minimum integrity level (reject Low / Untrusted).
        var ilCheck = CheckMinimumIntegrityLevel(handle, requiredLevel: SECURITY_MANDATORY_MEDIUM_RID);
        if (!ilCheck.IsValid)
            return ClientIdentityResult.Invalid(ilCheck.Reason!);

        // Step 6: optional Authenticode + thumbprint pinning.
        if (_pinnedSignerThumbprint is not null)
        {
            var sigCheck = VerifyAuthenticode(canonicalActual, _pinnedSignerThumbprint);
            if (!sigCheck.IsValid)
                return ClientIdentityResult.Invalid(sigCheck.Reason!);
        }

        return ClientIdentityResult.Valid(canonicalActual);
    }

    // ─────────────────────────────────────────────────────────────────────
    // PROCESS HANDLE
    // ─────────────────────────────────────────────────────────────────────

    private const uint PROCESS_QUERY_LIMITED_INFORMATION = 0x1000;

    private static SafeProcessHandle? OpenLimitedProcess(int pid)
    {
        var handle = OpenProcess(PROCESS_QUERY_LIMITED_INFORMATION, false, pid);
        if (handle == IntPtr.Zero)
            return null;
        return new SafeProcessHandle(handle, ownsHandle: true);
    }

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern IntPtr OpenProcess(uint dwDesiredAccess, [MarshalAs(UnmanagedType.Bool)] bool bInheritHandle, int dwProcessId);

    // ─────────────────────────────────────────────────────────────────────
    // IMAGE PATH (QueryFullProcessImageName — kernel-verified)
    // ─────────────────────────────────────────────────────────────────────

    private static string? QueryFullProcessImagePath(SafeProcessHandle handle)
    {
        var buffer = new char[1024];
        var size = buffer.Length;
        if (!QueryFullProcessImageName(handle, 0, buffer, ref size))
        {
            // Buffer too small → retry with a larger one
            size = 32_768;
            buffer = new char[size];
            if (!QueryFullProcessImageName(handle, 0, buffer, ref size))
                return null;
        }
        return new string(buffer, 0, size);
    }

    [DllImport("kernel32.dll", SetLastError = true, CharSet = CharSet.Unicode, EntryPoint = "QueryFullProcessImageNameW")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool QueryFullProcessImageName(
        SafeProcessHandle hProcess,
        uint dwFlags,
        [Out] char[] lpExeName,
        ref int lpdwSize);

    // ─────────────────────────────────────────────────────────────────────
    // INTEGRITY LEVEL
    // ─────────────────────────────────────────────────────────────────────

    private const uint TOKEN_QUERY = 0x0008;
    private const int TokenIntegrityLevel = 25;
    internal const uint SECURITY_MANDATORY_UNTRUSTED_RID = 0x0000_0000;
    internal const uint SECURITY_MANDATORY_LOW_RID = 0x0000_1000;
    internal const uint SECURITY_MANDATORY_MEDIUM_RID = 0x0000_2000;

    private static ClientIdentityResult CheckMinimumIntegrityLevel(SafeProcessHandle process, uint requiredLevel)
    {
        if (!OpenProcessToken(process, TOKEN_QUERY, out var tokenHandle))
            return ClientIdentityResult.Invalid($"OpenProcessToken failed: {Marshal.GetLastWin32Error()}");

        using (tokenHandle)
        {
            // First call to get required buffer size
            GetTokenInformation(tokenHandle, TokenIntegrityLevel, IntPtr.Zero, 0, out var size);
            if (size == 0)
                return ClientIdentityResult.Invalid("GetTokenInformation returned size 0");

            var buffer = Marshal.AllocHGlobal((int)size);
            try
            {
                if (!GetTokenInformation(tokenHandle, TokenIntegrityLevel, buffer, size, out _))
                    return ClientIdentityResult.Invalid($"GetTokenInformation failed: {Marshal.GetLastWin32Error()}");

                // TOKEN_MANDATORY_LABEL { SID_AND_ATTRIBUTES { PSID Sid, DWORD Attributes } }
                var pSid = Marshal.ReadIntPtr(buffer);
                var subAuthCountPtr = GetSidSubAuthorityCount(pSid);
                if (subAuthCountPtr == IntPtr.Zero)
                    return ClientIdentityResult.Invalid("GetSidSubAuthorityCount returned null");
                var subAuthCount = Marshal.ReadByte(subAuthCountPtr);
                if (subAuthCount == 0)
                    return ClientIdentityResult.Invalid("Integrity SID has zero sub-authorities");

                var lastSubAuthPtr = GetSidSubAuthority(pSid, (uint)(subAuthCount - 1));
                var integrityLevel = unchecked((uint)Marshal.ReadInt32(lastSubAuthPtr));

                if (integrityLevel < requiredLevel)
                    return ClientIdentityResult.Invalid(
                        $"Client integrity level 0x{integrityLevel:X} is below required 0x{requiredLevel:X} (Medium)");
            }
            finally
            {
                Marshal.FreeHGlobal(buffer);
            }
        }

        return ClientIdentityResult.Valid(string.Empty);
    }

    [DllImport("advapi32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool OpenProcessToken(SafeProcessHandle ProcessHandle, uint DesiredAccess, out SafeAccessTokenHandle TokenHandle);

    [DllImport("advapi32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GetTokenInformation(SafeAccessTokenHandle TokenHandle, int TokenInformationClass, IntPtr TokenInformation, uint TokenInformationLength, out uint ReturnLength);

    [DllImport("advapi32.dll", SetLastError = true)]
    private static extern IntPtr GetSidSubAuthority(IntPtr pSid, uint nSubAuthority);

    [DllImport("advapi32.dll", SetLastError = true)]
    private static extern IntPtr GetSidSubAuthorityCount(IntPtr pSid);

    // ─────────────────────────────────────────────────────────────────────
    // AUTHENTICODE
    // ─────────────────────────────────────────────────────────────────────

    private static ClientIdentityResult VerifyAuthenticode(string path, string pinnedThumbprint)
    {
        try
        {
            // X509Certificate.CreateFromSignedFile reads the WIN_CERTIFICATE
            // entry in the PE header — there is no .NET 10 replacement that
            // does the same thing in one call. (X509CertificateLoader loads
            // raw cert data, not Authenticode-extracted certs.) The SYSLIB0057
            // warning is about the generic Import/ctor pattern, not this PE
            // extractor specifically; suppress locally.
#pragma warning disable SYSLIB0057
            using var cert = System.Security.Cryptography.X509Certificates.X509Certificate.CreateFromSignedFile(path);
            using var cert2 = new System.Security.Cryptography.X509Certificates.X509Certificate2(cert);
#pragma warning restore SYSLIB0057
            var thumbprint = cert2.Thumbprint?.ToUpperInvariant();

            if (string.IsNullOrEmpty(thumbprint))
                return ClientIdentityResult.Invalid("Client binary signer thumbprint is empty");

            if (!string.Equals(thumbprint, pinnedThumbprint, StringComparison.Ordinal))
                return ClientIdentityResult.Invalid(
                    $"Client signer thumbprint '{thumbprint}' does not match pinned '{pinnedThumbprint}'");

            return ClientIdentityResult.Valid(string.Empty);
        }
        catch (CryptographicException ex)
        {
            return ClientIdentityResult.Invalid($"Client binary is not Authenticode-signed: {ex.Message}");
        }
        catch (Exception ex)
        {
            return ClientIdentityResult.Invalid($"Authenticode verification failed: {ex.Message}");
        }
    }
}
