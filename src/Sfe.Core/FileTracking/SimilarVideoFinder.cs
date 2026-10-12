using System.Globalization;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Sfe.Core.Data;
using Sfe.Core.Library;
using Sfe.Core.Options;
using Sfe.Core.Paths;
using Sfe.Core.Search;
using Sfe.Core.Tagging;
using Sfe.Core.Tasks;
using Sfe.Core.Trickplay;

namespace Sfe.Core.FileTracking;

public enum SimilarVideoState
{
    /// <summary>No comparison since the application started.</summary>
    Idle,
    Running,
    Completed,

    /// <summary>Stopped from the "Background tasks" dialog.</summary>
    Cancelled,
    Failed,
}

/// <param name="Checked">Videos whose fingerprint the comparison running (or the last one) has read.</param>
/// <param name="Total">Indexed videos that comparison looks at.</param>
/// <param name="Message">Why the last comparison failed.</param>
public sealed record SimilarVideoStatus(SimilarVideoState State, int Checked, int Total, DateTimeOffset? Started, DateTimeOffset? Finished, string? Message);

/// <param name="Match">Matching frames with the first video of the group, in percent of the longer one; null for that first video.</param>
/// <param name="Duration">Seconds, as seen by the trickplay (frames × interval).</param>
public sealed record SimilarVideo(LibraryItem File, int? Match, int Duration);

/// <param name="Count">Videos in the group; <paramref name="Videos"/> lists at most <see cref="DuplicateFinder.MaxFilesPerGroup"/>.</param>
/// <param name="WastedBytes">Space taken by all videos but the biggest.</param>
/// <param name="Videos">The video the others are compared to (the longest, then the biggest) first.</param>
public sealed record SimilarVideoGroup(int Count, long WastedBytes, IReadOnlyList<SimilarVideo> Videos);

/// <param name="Groups">Groups with a video in the folder asked for.</param>
/// <param name="Videos">Indexed videos in the whole library when the comparison ran.</param>
/// <param name="Compared">Of those, the videos that had a fingerprint (trickplay) and were compared.</param>
/// <param name="MinimumPercent">Match the comparison listed groups from.</param>
public sealed record SimilarVideoSummary(int Groups, int Files, long WastedBytes, int Videos, int Compared, int MinimumPercent);

/// <param name="Summary">Totals of the last comparison; only on the first page, null before any comparison.</param>
/// <param name="Next">Value of the <c>after</c> parameter that requests the next page; null on the last page.</param>
/// <param name="TrickplayEnabled">Trickplay is made in the background: videos get the fingerprints compared here.</param>
public sealed record SimilarVideoResults(
    string Path,
    SimilarVideoStatus Status,
    SimilarVideoSummary? Summary,
    IReadOnlyList<SimilarVideoGroup> Groups,
    bool HasMore,
    string? Next,
    bool TrickplayEnabled);

/// <summary>
/// Videos with the same pictures but other bytes: copies at another resolution, bitrate or codec, or trimmed, that
/// <see cref="DuplicateFinder"/> can't see. Compares the <see cref="VideoFingerprint"/>s of the indexed videos (made
/// with their trickplay) with <see cref="VideoMatcher"/>.
/// </summary>
/// <remarks>
/// A comparison reads one fingerprint per indexed video, so it runs in the background on request (like a scan) and
/// its groups are kept in memory until the next one: pages are cut from them.
/// </remarks>
public sealed class SimilarVideoFinder(
    IDbContextFactory<SfeDbContext> dbFactory,
    LibraryPaths paths,
    TrickplayService trickplay,
    DuplicateFinder duplicates,
    IOptionsMonitor<SfeOptions> options,
    TaskHistory history,
    ILogger<SimilarVideoFinder> logger) : IDisposable
{
    /// <summary>Under this, videos of one recording or of a still scene start to look alike.</summary>
    public const int LowestMinimumPercent = 50;

    private const int ReadParallelism = 4;

    private readonly Lock _gate = new();
    private readonly CancellationTokenSource _shutdown = new();
    private CancellationTokenSource? _current;
    private SimilarVideoState _state = SimilarVideoState.Idle;
    private int _checked;
    private int _total;
    private DateTimeOffset? _started;
    private DateTimeOffset? _finished;
    private string? _message;
    private int _runs;
    private Snapshot? _snapshot;

    /// <summary>The groups of a completed comparison; <see cref="Run"/> tells comparisons apart in page cursors.</summary>
    private sealed record Snapshot(int Run, IReadOnlyList<StoredGroup> Groups, int Videos, int Compared, int MinimumPercent);

    private sealed record StoredVideo(int Id, string FullName, long Size, int? Match, int Duration);

    private sealed record StoredGroup(IReadOnlyList<StoredVideo> Videos, long WastedBytes);

    /// <summary>The comparison running; for tests.</summary>
    internal Task Completion { get; private set; } = Task.CompletedTask;

    public SimilarVideoStatus Status
    {
        get
        {
            lock (_gate)
                return new SimilarVideoStatus(_state, _checked, _total, _started, _finished, _message);
        }
    }

    /// <summary>Starts comparing the indexed videos.</summary>
    /// <returns>false when a comparison is already running.</returns>
    public bool TryStart(TaskTrigger trigger = TaskTrigger.Manual, int? scheduleId = null)
    {
        lock (_gate)
        {
            if (_state == SimilarVideoState.Running)
                return false;
            _state = SimilarVideoState.Running;
            _checked = 0;
            _total = 0;
            _started = DateTimeOffset.Now;
            _finished = null;
            _message = null;
            var run = ++_runs;
            _current?.Dispose();
            _current = CancellationTokenSource.CreateLinkedTokenSource(_shutdown.Token);
            var token = _current.Token;
            Completion = Task.Run(() => RunAsync(run, trigger, scheduleId, token), CancellationToken.None);
            return true;
        }
    }

    /// <summary>Stops the comparison running; the groups of the previous one stay.</summary>
    /// <returns>false when none runs.</returns>
    public bool Cancel()
    {
        lock (_gate)
        {
            if (_state != SimilarVideoState.Running)
                return false;
            _current?.Cancel();
            return true;
        }
    }

    private async Task RunAsync(int run, TaskTrigger trigger, int? scheduleId, CancellationToken cancellationToken)
    {
        // From here on, what the comparison logs goes into the run's log.
        var record = history.Begin(BackgroundTaskKind.SimilarVideos, null, trigger, scheduleId);
        string? summary = null;
        var settings = options.CurrentValue;
        var minimumPercent = Math.Clamp(settings.FileTracking.SimilarVideos.MinimumPercent, LowestMinimumPercent, 100);
        logger.LogInformation("Comparison of the videos started");
        SimilarVideoState outcome;
        string? message = null;
        try
        {
            List<(int Id, string FullName, long Size)> videos;
            await using (var db = await dbFactory.CreateDbContextAsync(cancellationToken))
            {
                var rows = await db.Files.AsNoTracking()
                    .Where(SearchPredicateBuilder.HasExtension(settings.FileTypes.Videos))
                    .Select(f => new { f.Id, f.FullName, f.Size })
                    .ToListAsync(cancellationToken);
                // Rows of another root (the setting changed) are left out.
                videos = [.. rows.Where(r => paths.ToRelative(r.FullName) is not null).Select(r => (r.Id, r.FullName, r.Size))];
            }

            lock (_gate)
                _total = videos.Count;

            var fingerprints = new VideoFingerprint?[videos.Count];
            Parallel.For(0, videos.Count, new ParallelOptions { MaxDegreeOfParallelism = ReadParallelism, CancellationToken = cancellationToken }, i =>
            {
                fingerprints[i] = trickplay.GetFingerprint(videos[i].FullName);
                Interlocked.Increment(ref _checked);
            });

            var groups = new List<StoredGroup>();
            var compared = 0;
            // Frames of different intervals don't line up: only videos of one interval are compared.
            foreach (var interval in Enumerable.Range(0, videos.Count).Where(i => fingerprints[i] is not null).GroupBy(i => fingerprints[i]!.Interval))
            {
                List<int> indexes = [.. interval];
                compared += indexes.Count;
                var found = VideoMatcher.FindGroups(
                    [.. indexes.Select(i => fingerprints[i]!)], [.. indexes.Select(i => videos[i].Size)], minimumPercent, cancellationToken);
                foreach (var group in found)
                {
                    List<StoredVideo> members = [.. group.Members.Select(m =>
                    {
                        var i = indexes[m.Video];
                        var fingerprint = fingerprints[i]!;
                        return new StoredVideo(videos[i].Id, videos[i].FullName, videos[i].Size, m.Percent, fingerprint.Count * fingerprint.Interval);
                    })];
                    groups.Add(new StoredGroup(members, members.Sum(v => v.Size) - members.Max(v => v.Size)));
                }
            }

            // Most space to win back first, like the duplicates.
            groups.Sort((a, b) => b.WastedBytes != a.WastedBytes
                ? b.WastedBytes.CompareTo(a.WastedBytes)
                : string.CompareOrdinal(a.Videos[0].FullName, b.Videos[0].FullName));
            _snapshot = new Snapshot(run, groups, videos.Count, compared, minimumPercent);
            outcome = SimilarVideoState.Completed;
            logger.LogInformation("Comparison of the videos finished: {Compared} of {Videos} videos compared, {Groups} groups of similar videos",
                compared, videos.Count, groups.Count);
            summary = $"{compared:N0} of {videos.Count:N0} videos compared, {groups.Count:N0} groups of similar videos";
        }
        catch (OperationCanceledException) when (!_shutdown.IsCancellationRequested)
        {
            logger.LogInformation("Comparison of the videos cancelled");
            outcome = SimilarVideoState.Cancelled;
        }
        catch (OperationCanceledException)
        {
            // Application shutdown.
            outcome = SimilarVideoState.Idle;
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Comparison of the videos failed");
            outcome = SimilarVideoState.Failed;
            message = ex.Message;
        }

        lock (_gate)
        {
            _state = outcome;
            _message = message;
            _finished = DateTimeOffset.Now;
        }

        await record.CompleteAsync(outcome switch
        {
            SimilarVideoState.Completed => TaskOutcome.Completed,
            SimilarVideoState.Cancelled => TaskOutcome.Cancelled,
            SimilarVideoState.Failed => TaskOutcome.Failed,
            _ => TaskOutcome.Interrupted,
        }, summary ?? message);
    }

    /// <param name="relativeDirectory">Only groups with a video in this folder (or below) are listed; empty for all.</param>
    /// <param name="after"><see cref="SimilarVideoResults.Next"/> of the previous page; null for the first one.</param>
    /// <returns>null when <paramref name="relativeDirectory"/> is invalid, or <paramref name="after"/> is not a page of the last comparison.</returns>
    public async Task<SimilarVideoResults?> FindAsync(string? relativeDirectory, string? after, int? limit, CancellationToken cancellationToken = default)
    {
        if (!paths.TryResolve(relativeDirectory, out var directory))
            return null;

        var status = Status;
        var snapshot = _snapshot;
        var path = paths.ToRelative(directory) ?? string.Empty;
        var trickplayEnabled = options.CurrentValue.Trickplay.Enabled;
        if (snapshot is null)
            return after is null ? new SimilarVideoResults(path, status, null, [], false, null, trickplayEnabled) : null;

        var start = 0;
        if (after is not null)
        {
            var colon = after.IndexOf(':');
            if (colon <= 0
                || !int.TryParse(after.AsSpan(0, colon), NumberStyles.None, CultureInfo.InvariantCulture, out var run) || run != snapshot.Run
                || !int.TryParse(after.AsSpan(colon + 1), NumberStyles.None, CultureInfo.InvariantCulture, out start))
                return null;
        }

        var prefix = Path.TrimEndingDirectorySeparator(directory) + Path.DirectorySeparatorChar;
        var groups = path is ""
            ? snapshot.Groups
            : [.. snapshot.Groups.Where(g => g.Videos.Any(v => v.FullName.StartsWith(prefix, LibraryPaths.PathComparison)))];
        var pageSize = Math.Clamp(limit ?? options.CurrentValue.FileTracking.Duplicates.GroupsPerPage, 1, DuplicateFinder.MaxGroupsPerPage);
        var page = groups.Skip(start).Take(pageSize).ToList();

        var summary = after is null
            ? new SimilarVideoSummary(groups.Count, groups.Sum(g => g.Videos.Count), groups.Sum(g => g.WastedBytes), snapshot.Videos, snapshot.Compared, snapshot.MinimumPercent)
            : null;
        var results = await LoadAsync(page, cancellationToken);
        var hasMore = start + page.Count < groups.Count;
        return new SimilarVideoResults(
            Path: path,
            Status: status,
            Summary: summary,
            Groups: results,
            HasMore: hasMore,
            Next: hasMore ? $"{snapshot.Run}:{start + page.Count}" : null,
            TrickplayEnabled: trickplayEnabled);
    }

    /// <summary>The groups' videos as they are indexed now, with their tags; videos no longer indexed are left out.</summary>
    private async Task<List<SimilarVideoGroup>> LoadAsync(List<StoredGroup> page, CancellationToken cancellationToken)
    {
        List<int> ids = [.. page.SelectMany(g => g.Videos.Take(DuplicateFinder.MaxFilesPerGroup)).Select(v => v.Id)];
        await using var db = await dbFactory.CreateDbContextAsync(cancellationToken);
        var rows = await db.Files.AsNoTracking()
            .Where(f => ids.Contains(f.Id))
            .Select(f => new { f.Id, f.FullName, f.Extension, f.Size, f.LastWriteTime, TagIds = f.TagLinks.Select(l => l.TagId).ToList() })
            .ToDictionaryAsync(r => r.Id, cancellationToken);
        var graph = await TagGraph.LoadAsync(db, cancellationToken);

        var results = new List<SimilarVideoGroup>(page.Count);
        foreach (var group in page)
        {
            List<SimilarVideo> videos = [];
            foreach (var video in group.Videos.Take(DuplicateFinder.MaxFilesPerGroup))
            {
                if (rows.TryGetValue(video.Id, out var row)
                    && string.Equals(row.FullName, video.FullName, LibraryPaths.PathComparison)
                    && duplicates.ToItem(row.FullName, row.Extension, row.Size, row.LastWriteTime, TagService.Summaries(graph, row.TagIds)) is { } item)
                    videos.Add(new SimilarVideo(item, video.Match, video.Duration));
            }

            if (videos.Count > 1)
                results.Add(new SimilarVideoGroup(group.Videos.Count, group.WastedBytes, videos));
        }

        return results;
    }

    public void Dispose()
    {
        _shutdown.Cancel();
        _shutdown.Dispose();
        _current?.Dispose();
    }
}
