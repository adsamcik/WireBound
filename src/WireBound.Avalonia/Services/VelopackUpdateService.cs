using System.Net.Http;
using System.Reflection;
using Serilog;
using Velopack;
using WireBound.Core.Services;

namespace WireBound.Avalonia.Services;

/// <summary>Checks aged GitHub releases and verifies packages before installation.</summary>
public sealed class VelopackUpdateService : IUpdateService
{
    private static readonly HttpClient HttpClient = new()
    {
        DefaultRequestHeaders =
        {
            { "User-Agent", "WireBound-UpdateChecker" },
            { "Accept", "application/vnd.github+json" },
            { "Cache-Control", "no-cache" }
        },
        Timeout = TimeSpan.FromSeconds(10)
    };

    private readonly VerifiedUpdateManager _updateManager;
    private readonly GitHubUpdateSource _source;
    private readonly SemaphoreSlim _operationLock = new(1, 1);

    public VelopackUpdateService(TimeProvider timeProvider)
    {
        _source = new GitHubUpdateSource(HttpClient, timeProvider);
        _updateManager = new VerifiedUpdateManager(_source);
    }

    internal VelopackUpdateService(VerifiedUpdateManager updateManager, GitHubUpdateSource source)
    {
        _updateManager = updateManager;
        _source = source;
    }

    public bool IsUpdateSupported => _updateManager.IsInstalled;

    public string CurrentVersion => _updateManager.CurrentVersion?.ToString()
        ?? Assembly.GetEntryAssembly()?.GetName().Version?.ToString(3)
        ?? "0.0.0";

    // A local file alone is insufficient: it must match an eligible remote release and checksum.
    public UpdateCheckResult? PreparedUpdate { get; private set; }

    public async Task<UpdateCheckResult?> CheckForUpdateAsync(CancellationToken cancellationToken = default)
    {
        await _operationLock.WaitAsync(cancellationToken);
        try
        {
            PreparedUpdate = null;
            if (!IsUpdateSupported)
                return await _source.CheckPortableAsync(CurrentVersion, cancellationToken);

            UpdateInfo? info;
            try
            {
                // Velopack's check has no token parameter; its network requests are bounded.
                info = await _updateManager.CheckForUpdatesAsync();
                cancellationToken.ThrowIfCancellationRequested();
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested) { throw; }
            catch (Exception ex)
            {
                cancellationToken.ThrowIfCancellationRequested();
                Log.Warning(ex, "Velopack update check failed, falling back to aged GitHub releases");
                return await _source.CheckPortableAsync(CurrentVersion, cancellationToken);
            }

            if (info is null) return null;
            var target = info.TargetFullRelease;
            await _source.RevalidateAsync(target, cancellationToken);
            var update = new UpdateCheckResult(target.Version.ToString(),
                _source.GetReleaseUrl(target), _source.GetPublishedAt(target), info, CanInstallInApp: true);

            if (_updateManager.UpdatePendingRestart is { } local && local.FileName == target.FileName)
            {
                try
                {
                    await _updateManager.VerifyLocalPackageAsync(target);
                    cancellationToken.ThrowIfCancellationRequested();
                    PreparedUpdate = update;
                }
                catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested) { throw; }
                catch (Exception ex)
                {
                    Log.Warning(ex, "Cached update verification failed; it will not be installed");
                }
            }
            return update;
        }
        finally { _operationLock.Release(); }
    }

    public async Task DownloadUpdateAsync(UpdateCheckResult update, Action<int>? progress = null,
        CancellationToken cancellationToken = default)
    {
        await _operationLock.WaitAsync(cancellationToken);
        try
        {
            var info = GetInstallableInfo(update);
            PreparedUpdate = null;
            await _source.RevalidateAsync(info.TargetFullRelease, cancellationToken);
            await _updateManager.DownloadUpdatesAsync(info, progress, cancellationToken);
            cancellationToken.ThrowIfCancellationRequested();
            PreparedUpdate = update;
        }
        finally { _operationLock.Release(); }
    }

    public async Task ApplyUpdateAndRestartAsync(UpdateCheckResult update, CancellationToken cancellationToken = default)
    {
        await _operationLock.WaitAsync(cancellationToken);
        try
        {
            var info = GetInstallableInfo(update);
            await _source.RevalidateAsync(info.TargetFullRelease, cancellationToken);
            await _updateManager.VerifyLocalPackageAsync(info.TargetFullRelease);
            cancellationToken.ThrowIfCancellationRequested();
            _updateManager.ApplyAndRestart(info.TargetFullRelease);
        }
        finally { _operationLock.Release(); }
    }

    private UpdateInfo GetInstallableInfo(UpdateCheckResult update)
    {
        if (!IsUpdateSupported || !update.CanInstallInApp)
            throw new InvalidOperationException("This update must be installed from the release download page.");
        if (update.NativeUpdateInfo is not UpdateInfo info || update.Version != info.TargetFullRelease.Version.ToString() || info.IsDowngrade)
            throw new ArgumentException("Invalid update information. Check for updates again.", nameof(update));
        return info;
    }
}
