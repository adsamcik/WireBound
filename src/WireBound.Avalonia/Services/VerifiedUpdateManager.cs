using Velopack;
using Velopack.Exceptions;
using Velopack.Locators;
using Velopack.Sources;

namespace WireBound.Avalonia.Services;

/// <summary>Also verifies cached and reconstructed packages before they can supply an updater executable.</summary>
internal class VerifiedUpdateManager(IUpdateSource source, IVelopackLocator? locator = null) : UpdateManager(source, locator: locator)
{
    public virtual Task VerifyLocalPackageAsync(VelopackAsset asset) => VerifyPackageChecksumAsync(asset);

    public virtual void ApplyAndRestart(VelopackAsset asset) => ApplyUpdatesAndRestart(asset);

    public override async Task DownloadUpdatesAsync(UpdateInfo updates, Action<int>? progress = null,
        CancellationToken cancelToken = default)
    {
        var path = Path.Combine(Locator.PackagesDir!, updates.TargetFullRelease.FileName);
        if (File.Exists(path))
        {
            try { await VerifyLocalPackageAsync(updates.TargetFullRelease).ConfigureAwait(false); }
            catch (ChecksumFailedException)
            {
                // Delete only this corrupt cache entry so retry can download a fresh copy.
                File.Delete(path);
            }
        }
        await base.DownloadUpdatesAsync(updates, progress, cancelToken).ConfigureAwait(false);
        await VerifyLocalPackageAsync(updates.TargetFullRelease).ConfigureAwait(false);
    }

    protected override async Task DownloadAndApplyDeltaUpdates(UpdateInfo updates, string targetFile,
        Action<int> progress, CancellationToken cancelToken)
    {
        await base.DownloadAndApplyDeltaUpdates(updates, targetFile, progress, cancelToken).ConfigureAwait(false);
        await VerifyPackageChecksumAsync(updates.TargetFullRelease, targetFile).ConfigureAwait(false);
    }
}
