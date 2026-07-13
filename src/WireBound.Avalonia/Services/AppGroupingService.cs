using WireBound.Core.Services;

namespace WireBound.Avalonia.Services;

/// <summary>
/// Heuristic grouper for related applications shown on the Apps page.
///
/// <para>
/// Apps that share an install directory AND have a meaningful common name
/// prefix (e.g. <c>bridge</c> + <c>bridge-gui</c>, <c>vlc</c> + <c>vlc-cache</c>,
/// <c>Discord</c> + <c>DiscordHelper</c>) are clustered into a single
/// expandable row. The group's head is a SYNTHESIZED <see cref="AppOverview"/>
/// carrying aggregated stats from all members; the originals remain available
/// as the head's <see cref="GroupResult.Members"/> so the view can render
/// them as indented child rows when the user expands the group.
/// </para>
///
/// <para>
/// Heuristics are intentionally conservative: a wrong grouping confuses the
/// user more than missing one. If in doubt, leave the apps standalone.
/// </para>
/// </summary>
public sealed class AppGroupingService
{
    /// <summary>
    /// Minimum length of the common name prefix two apps must share before
    /// they're considered candidates for the same group. 3 chars catches
    /// <c>vlc</c> + <c>vlc-cache</c> but rejects spurious one-letter
    /// matches between unrelated executables.
    /// </summary>
    private const int MinCommonPrefixLength = 3;

    /// <summary>
    /// Apply grouping to a flat list of overviews. Returns a structure that
    /// the view-model can flatten with the user's expansion state.
    /// </summary>
    public IReadOnlyList<GroupResult> Group(IReadOnlyList<AppOverview> apps)
    {
        if (apps.Count <= 1) return ToSolo(apps);

        // Cluster by canonical install directory first. Apps in different
        // directories are never grouped, even if their names match (different
        // <c>chrome</c> installs are different products).
        var byInstallDir = new Dictionary<string, List<AppOverview>>(StringComparer.OrdinalIgnoreCase);
        var solos = new List<AppOverview>();
        foreach (var app in apps)
        {
            var dir = NormalizeInstallDir(app.ExecutablePath);
            if (dir is null)
            {
                // No install dir = no grouping signal. Treat as solo.
                solos.Add(app);
                continue;
            }
            if (!byInstallDir.TryGetValue(dir, out var bucket))
            {
                bucket = new List<AppOverview>();
                byInstallDir[dir] = bucket;
            }
            bucket.Add(app);
        }

        var results = new List<GroupResult>(apps.Count);

        foreach (var (dir, bucket) in byInstallDir)
        {
            // Within an install dir, sub-cluster by common name prefix.
            var subClusters = ClusterByNamePrefix(bucket);
            foreach (var cluster in subClusters)
            {
                if (cluster.Count < 2)
                {
                    // Singleton inside the install dir → solo render.
                    foreach (var app in cluster) results.Add(GroupResult.Solo(app));
                }
                else
                {
                    var head = BuildAggregateHead(cluster, dir);
                    results.Add(new GroupResult(head, cluster));
                }
            }
        }

        foreach (var solo in solos) results.Add(GroupResult.Solo(solo));

        return results;
    }

    private static IReadOnlyList<GroupResult> ToSolo(IReadOnlyList<AppOverview> apps) =>
        apps.Select(GroupResult.Solo).ToList();

    /// <summary>
    /// Sub-cluster apps in an install directory by shared name prefix. Uses
    /// the longest common prefix among the cluster's apps as the grouping
    /// key. Apps whose normalized name doesn't share at least
    /// <see cref="MinCommonPrefixLength"/> chars with any other are left
    /// alone (one-app clusters).
    /// </summary>
    private static List<List<AppOverview>> ClusterByNamePrefix(List<AppOverview> apps)
    {
        var clusters = new List<List<AppOverview>>();
        var assigned = new bool[apps.Count];

        for (var i = 0; i < apps.Count; i++)
        {
            if (assigned[i]) continue;
            var cluster = new List<AppOverview> { apps[i] };
            assigned[i] = true;
            var anchorName = NormalizeName(apps[i].ProcessName, apps[i].ExecutablePath);

            for (var j = i + 1; j < apps.Count; j++)
            {
                if (assigned[j]) continue;
                var candidateName = NormalizeName(apps[j].ProcessName, apps[j].ExecutablePath);
                if (HaveSharedPrefix(anchorName, candidateName, MinCommonPrefixLength))
                {
                    cluster.Add(apps[j]);
                    assigned[j] = true;
                }
            }
            clusters.Add(cluster);
        }
        return clusters;
    }

    /// <summary>
    /// True iff <paramref name="a"/> and <paramref name="b"/> share a leading
    /// substring of at least <paramref name="minLength"/> characters and the
    /// shared prefix is followed by a meaningful boundary (separator, end of
    /// string, or extended length). The boundary check prevents accidentally
    /// pairing <c>caches</c> with <c>cachet</c> just because they share 5
    /// letters — there must be a logical "stem + variant" relationship.
    /// </summary>
    private static bool HaveSharedPrefix(string a, string b, int minLength)
    {
        if (a.Length < minLength || b.Length < minLength) return false;
        var max = Math.Min(a.Length, b.Length);
        var common = 0;
        for (var i = 0; i < max; i++)
        {
            if (a[i] != b[i]) break;
            common++;
        }
        if (common < minLength) return false;

        // Reject when the common prefix doesn't end at a logical boundary in
        // BOTH names. Allowed boundaries: end-of-string, separator char.
        return EndsAtBoundary(a, common) && EndsAtBoundary(b, common);
    }

    private static bool EndsAtBoundary(string s, int idx)
    {
        if (idx >= s.Length) return true;
        var c = s[idx];
        return c is '-' or '_' or '.' or ' ';
    }

    /// <summary>
    /// Canonicalize the executable's install directory so case + trailing
    /// separator + relative-path noise don't fracture the bucketing.
    /// Returns null when the app has no resolvable path.
    /// </summary>
    private static string? NormalizeInstallDir(string? exePath)
    {
        if (string.IsNullOrWhiteSpace(exePath)) return null;
        try
        {
            var dir = Path.GetDirectoryName(Path.GetFullPath(exePath));
            return string.IsNullOrWhiteSpace(dir) ? null : dir;
        }
        catch
        {
            return null;
        }
    }

    /// <summary>
    /// Reduce the app's process / executable name to a comparable token.
    /// Strips extension, trims surrounding whitespace, lowercases for
    /// case-insensitive prefix matching.
    /// </summary>
    private static string NormalizeName(string processName, string? exePath)
    {
        var source = !string.IsNullOrWhiteSpace(processName)
            ? processName
            : (!string.IsNullOrWhiteSpace(exePath) ? Path.GetFileName(exePath) : string.Empty);
        var withoutExt = Path.GetFileNameWithoutExtension(source) ?? string.Empty;
        return withoutExt.Trim().ToLowerInvariant();
    }

    /// <summary>
    /// Build the synthesized head row for a group: aggregated stats, the
    /// most-representative member's name/icon/category, and IsGroupHead set.
    /// </summary>
    private static AppOverview BuildAggregateHead(List<AppOverview> members, string installDir)
    {
        // Representative is the member with the shortest normalized name —
        // "bridge" is the head, "bridge-gui" is the child. Ties go to highest
        // total bytes so the head reflects the busiest variant.
        var representative = members
            .OrderBy(m => NormalizeName(m.ProcessName, m.ExecutablePath).Length)
            .ThenByDescending(m => m.TotalBytes)
            .First();

        var groupKey = ComputeGroupKey(installDir, representative);

        var totalReceived = members.Sum(m => m.BytesReceived);
        var totalSent = members.Sum(m => m.BytesSent);
        var peakDown = members.Max(m => m.PeakDownloadSpeed);
        var peakUp = members.Max(m => m.PeakUploadSpeed);
        // Weighted average CPU by hours active — busiest member dominates,
        // which is the intuition users have when reading a "Chrome" row.
        var totalHours = members.Sum(m => Math.Max(m.HoursActive, 1));
        var weightedAvgCpu = members.Sum(m => m.AvgCpuPercent * Math.Max(m.HoursActive, 1)) / totalHours;
        var maxCpu = members.Max(m => m.MaxCpuPercent);
        // RAM is per-process, so the group's footprint is the sum of member
        // averages (a Chrome group with 8 processes really does use ~8x).
        var sumPrivateBytes = members.Sum(m => m.AvgPrivateBytes);
        var peakPrivateBytes = members.Max(m => m.PeakPrivateBytes);
        var firstSeen = members.Min(m => m.FirstSeen);
        var lastSeen = members.Max(m => m.LastSeen);
        var hoursActive = members.Max(m => m.HoursActive);

        var head = new AppOverview(
            // Use a synthetic identifier so the head and its representative
            // member don't collide on selection / scroll-into-view.
            $"group:{groupKey}",
            representative.AppName,
            representative.ProcessName,
            representative.ExecutablePath,
            representative.CategoryName,
            totalReceived,
            totalSent,
            peakDown,
            peakUp,
            weightedAvgCpu,
            maxCpu,
            sumPrivateBytes,
            peakPrivateBytes,
            firstSeen,
            lastSeen,
            hoursActive)
        {
            IconPath = representative.IconPath,
            GroupKey = groupKey,
            IsGroupHead = true,
            GroupMemberCount = members.Count,
        };

        // Propagate live CPU + RAM from the representative so the head's
        // "live" columns show the busiest variant's value when available.
        // The next live-refresh tick (RefreshLiveCpuValues) will overwrite
        // these with the SUM of all members — this is just the bootstrap so
        // the row isn't blank until the next timer tick.
        if (representative.HasLiveCpu)
            head.LiveCpuPercent = representative.LiveCpuPercent;
        if (representative.HasLiveRam)
            head.LiveRamBytes = representative.LiveRamBytes;

        return head;
    }

    private static string ComputeGroupKey(string installDir, AppOverview representative)
    {
        // Combine install dir + representative name so the key is stable
        // across reloads but uniquely identifies the cluster.
        var name = NormalizeName(representative.ProcessName, representative.ExecutablePath);
        return $"{installDir}|{name}";
    }

    /// <summary>
    /// One grouping outcome — either a solo app (Members is empty) or a
    /// head + N members.
    /// </summary>
    public sealed record GroupResult(AppOverview Head, IReadOnlyList<AppOverview> Members)
    {
        public bool IsGroup => Members.Count > 1;

        /// <summary>
        /// Construct a "group of one" result that wraps a solo app. The Head
        /// is the app itself with no grouping metadata; Members is a
        /// single-element list containing the same app.
        /// </summary>
        public static GroupResult Solo(AppOverview app) => new(app, new[] { app });
    }
}
