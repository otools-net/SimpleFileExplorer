using Sfe.Core.Files;
using Sfe.Core.FileTracking;
using Sfe.Core.Indexing;
using Sfe.Core.Paths;
using Sfe.Core.Thumbnails;
using Sfe.Core.Trickplay;

namespace Sfe.Core.Tasks;

/// <summary>
/// One place to see, start and stop the background jobs, each of which keeps running its own way: index scans,
/// trickplay passes, similar-video comparisons, the trash's purge, and the thumbnails asked for by the listings.
/// </summary>
public sealed class BackgroundTasks(
    ScanCoordinator scan,
    TrickplayService trickplay,
    SimilarVideoFinder similarVideos,
    ThumbnailService thumbnails,
    Trash trash,
    TaskHistory history)
{
    /// <summary>The tasks schedules can start.</summary>
    public static bool CanSchedule(BackgroundTaskKind task) => task is not BackgroundTaskKind.Thumbnails;

    public async Task<IReadOnlyList<TaskInfo>> DescribeAsync(CancellationToken cancellationToken = default)
    {
        var last = await history.LastRunsAsync(cancellationToken);
        return [.. Enum.GetValues<BackgroundTaskKind>().Select(kind => Describe(kind, last.GetValueOrDefault(kind)))];
    }

    private TaskInfo Describe(BackgroundTaskKind kind, TaskRunSummary? last) => kind switch
    {
        BackgroundTaskKind.Scan => DescribeScan(last),
        BackgroundTaskKind.Trickplay => DescribeTrickplay(last),
        BackgroundTaskKind.SimilarVideos => DescribeSimilarVideos(last),
        BackgroundTaskKind.TrashPurge => DescribeTrashPurge(last),
        _ => DescribeThumbnails(last),
    };

    private TaskInfo DescribeTrashPurge(TaskRunSummary? last)
    {
        var status = trash.PurgeStatus;
        var running = status.State == TrashPurgeState.Running;
        var days = trash.RetentionDays;
        var detail = running ? $"{status.Purged:N0} deleted for good so far"
            : last is null ? days == 0 ? "The trash keeps everything until it is emptied." : $"Deletes for good what has been in the trash more than {days} days."
            : LastResult(last);
        return new TaskInfo(
            BackgroundTaskKind.TrashPurge, running ? TaskState.Running : TaskState.Idle, null, running ? status.Started : null, null, detail,
            CanStart: !running, CanCancel: running, CanSchedule: true, null, null, last);
    }

    private TaskInfo DescribeScan(TaskRunSummary? last)
    {
        var status = scan.Status;
        var running = status.State == ScanState.Running;
        var detail = running
            ? ScanCoordinator.Describe(status.DirectoriesScanned, status.DirectoriesQueued, status.FilesIndexed, status.Removed, status.Relinked, status.Errors)
                + (status.CurrentDirectory is { Length: > 0 } current ? $" — {current}" : string.Empty)
            : LastResult(last);
        // Folders are found level by level: the share is only a rough idea, but it moves.
        double? progress = running && status.DirectoriesScanned + status.DirectoriesQueued > 0
            ? (double)status.DirectoriesScanned / (status.DirectoriesScanned + status.DirectoriesQueued)
            : null;
        return new TaskInfo(
            BackgroundTaskKind.Scan, running ? TaskState.Running : TaskState.Idle, status.Path, running ? status.StartedAt : null,
            progress, detail, CanStart: !running, CanCancel: running, CanSchedule: true, null, null, last);
    }

    private TaskInfo DescribeTrickplay(TaskRunSummary? last)
    {
        var status = trickplay.Status;
        var state = status.State switch
        {
            TrickplayState.Disabled => TaskState.Disabled,
            TrickplayState.Unavailable => TaskState.Unavailable,
            TrickplayState.Running => TaskState.Running,
            _ => TaskState.Idle,
        };
        var left = status.Current.Count + status.Queued + status.Pending;
        var done = status.Generated + status.Failed;
        var detail = state switch
        {
            TaskState.Disabled => "Turned off in Administration › Trickplay › Settings.",
            TaskState.Unavailable => "ffmpeg can't be found.",
            TaskState.Running when status.Discovering => $"Looking for videos without trickplay: {status.Checked:N0} checked, {status.Pending:N0} waiting",
            TaskState.Running => $"{left:N0} videos left, {status.Generated:N0} created, {status.Failed:N0} failed",
            _ => LastResult(last),
        };
        double? progress = state == TaskState.Running && !status.Discovering && done + left > 0 ? (double)done / (done + left) : null;
        var every = trickplay.IsAvailable && status.NextPass is not null ? trickplay.RecheckHours : (int?)null;
        return new TaskInfo(
            BackgroundTaskKind.Trickplay, state, null, state == TaskState.Running ? status.PassStarted : null, progress, detail,
            CanStart: trickplay.IsAvailable, CanCancel: state == TaskState.Running, CanSchedule: true,
            status.NextPass, every, last);
    }

    private TaskInfo DescribeSimilarVideos(TaskRunSummary? last)
    {
        var status = similarVideos.Status;
        var running = status.State == SimilarVideoState.Running;
        var detail = running
            ? status.Total > 0 ? $"{status.Checked:N0} of {status.Total:N0} videos read" : "Listing the indexed videos"
            : LastResult(last);
        double? progress = running && status.Total > 0 ? (double)status.Checked / status.Total : null;
        return new TaskInfo(
            BackgroundTaskKind.SimilarVideos, running ? TaskState.Running : TaskState.Idle, null, running ? status.Started : null,
            progress, detail, CanStart: !running, CanCancel: running, CanSchedule: true, null, null, last);
    }

    private TaskInfo DescribeThumbnails(TaskRunSummary? last)
    {
        var (rendering, waiting) = thumbnails.Activity;
        var busy = rendering + waiting > 0;
        var state = !thumbnails.IsEnabled ? TaskState.Disabled
            : !thumbnails.IsAvailable ? TaskState.Unavailable
            : busy ? TaskState.Running
            : TaskState.Idle;
        var detail = state switch
        {
            TaskState.Disabled => "Turned off in Administration › Thumbnails.",
            TaskState.Unavailable => "ffmpeg can't be found.",
            TaskState.Running => $"{rendering:N0} being rendered, {waiting:N0} waiting",
            _ => "Rendered when a listing shows a photo or video without one.",
        };
        return new TaskInfo(
            BackgroundTaskKind.Thumbnails, state, null, null, null, detail,
            CanStart: false, CanCancel: busy, CanSchedule: false, null, null, last);
    }

    private static string LastResult(TaskRunSummary? last) => last switch
    {
        null => "Not run yet.",
        { Outcome: TaskOutcome.Running } => "Starting…",
        { Summary: { Length: > 0 } summary } => summary,
        _ => last.Outcome.ToString(),
    };

    /// <param name="path">Folder to scan, relative to the library root; ignored by the other tasks.</param>
    public TaskStartResult Start(BackgroundTaskKind task, string? path, TaskTrigger trigger, int? scheduleId = null)
    {
        switch (task)
        {
            case BackgroundTaskKind.Scan:
                return scan.TryStart(LibraryPaths.NormalizeRelative(path) ?? string.Empty, trigger, scheduleId) ? TaskStartResult.Started : TaskStartResult.AlreadyRunning;
            case BackgroundTaskKind.Trickplay:
                var running = trickplay.Status.State == TrickplayState.Running;
                return !trickplay.RequestPass(trigger, scheduleId) ? TaskStartResult.Unavailable
                    : running ? TaskStartResult.AlreadyRunning
                    : TaskStartResult.Started;
            case BackgroundTaskKind.SimilarVideos:
                return similarVideos.TryStart(trigger, scheduleId) ? TaskStartResult.Started : TaskStartResult.AlreadyRunning;
            case BackgroundTaskKind.TrashPurge:
                return trash.TryStartPurge(trigger, scheduleId) ? TaskStartResult.Started : TaskStartResult.AlreadyRunning;
            default:
                return TaskStartResult.Unavailable;
        }
    }

    /// <returns>false when the task wasn't running.</returns>
    public bool Cancel(BackgroundTaskKind task) => task switch
    {
        BackgroundTaskKind.Scan => scan.Cancel(),
        BackgroundTaskKind.Trickplay => trickplay.Cancel(),
        BackgroundTaskKind.SimilarVideos => similarVideos.Cancel(),
        BackgroundTaskKind.TrashPurge => trash.CancelPurge(),
        _ => thumbnails.CancelAll(),
    };
}
