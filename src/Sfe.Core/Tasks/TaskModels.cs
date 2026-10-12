namespace Sfe.Core.Tasks;

/// <summary>The jobs that run in the background, shown together in the client's "Background tasks" dialog.</summary>
public enum BackgroundTaskKind
{
    /// <summary>Index scan of a folder (<see cref="Indexing.ScanCoordinator"/>).</summary>
    Scan,

    /// <summary>Pass over the trickplay folders (<see cref="Trickplay.TrickplayService"/>).</summary>
    Trickplay,

    /// <summary>Comparison of the indexed videos (<see cref="FileTracking.SimilarVideoFinder"/>).</summary>
    SimilarVideos,

    /// <summary>Thumbnails requested by the listings: shown and cancelled, but neither started nor scheduled.</summary>
    Thumbnails,
}

public enum TaskState
{
    Idle,
    Running,

    /// <summary>Turned off in the settings.</summary>
    Disabled,

    /// <summary>Turned on, but ffmpeg can't be found.</summary>
    Unavailable,
}

/// <summary>How a run of a task ended.</summary>
public enum TaskOutcome
{
    Running,
    Completed,
    Cancelled,
    Failed,

    /// <summary>The server stopped while it ran.</summary>
    Interrupted,

    /// <summary>A schedule came due while the task was already running, or could not run (e.g. turned off).</summary>
    Skipped,
}

/// <summary>What started a run.</summary>
public enum TaskTrigger
{
    /// <summary>Someone, from the web client or the API.</summary>
    Manual,

    /// <summary>A schedule of the "Background tasks" dialog.</summary>
    Schedule,

    /// <summary>The task itself: trickplay at start-up, after a settings change, every <c>RecheckHours</c>.</summary>
    Automatic,
}

/// <summary>When a schedule runs its task.</summary>
public enum ScheduleKind
{
    /// <summary>Once, at its start.</summary>
    Once,

    /// <summary>At its start, then every <c>IntervalMinutes</c>.</summary>
    Interval,

    /// <summary>On the days of <c>Days</c>, at the time of day of its start (server time); every day = all days.</summary>
    Weekly,
}

/// <summary>A task as the "Background tasks" dialog shows it.</summary>
/// <param name="Path">Folder of the scan running (or last run), relative to the library root.</param>
/// <param name="Progress">Share done, from 0 to 1, when the total is known.</param>
/// <param name="Detail">What it is doing, or how it last ended.</param>
/// <param name="CanStart">It can be started now (from the dialog, or by a schedule).</param>
/// <param name="CanSchedule">Schedules can start it.</param>
/// <param name="AutomaticNextRun">When the task will run by itself next (trickplay's periodic check), whatever the schedules.</param>
/// <param name="AutomaticEvery">Hours between those runs; null when it doesn't run by itself.</param>
/// <param name="LastRun">Its last recorded run, running or finished.</param>
public sealed record TaskInfo(
    BackgroundTaskKind Task,
    TaskState State,
    string? Path,
    DateTimeOffset? StartedAt,
    double? Progress,
    string Detail,
    bool CanStart,
    bool CanCancel,
    bool CanSchedule,
    DateTimeOffset? AutomaticNextRun,
    int? AutomaticEvery,
    TaskRunSummary? LastRun);

/// <summary>Result of starting a task.</summary>
public enum TaskStartResult
{
    Started,

    /// <summary>Already running. Trickplay takes the request anyway: a new pass follows the current one.</summary>
    AlreadyRunning,

    /// <summary>Turned off, ffmpeg missing, or a task that can't be started.</summary>
    Unavailable,
}

/// <param name="Path">Folder a scan covers, relative to the library root; null for the other tasks.</param>
/// <param name="ScheduleId">Schedule that started the run.</param>
public record TaskRunSummary(
    long Id,
    BackgroundTaskKind Task,
    string? Path,
    TaskTrigger Trigger,
    int? ScheduleId,
    DateTimeOffset Started,
    DateTimeOffset? Finished,
    TaskOutcome Outcome,
    string? Summary);

/// <summary>A line of a run's log: what the task logged (from Debug up) while it ran.</summary>
public sealed record TaskLogLine(DateTimeOffset Time, string Level, string Message);

/// <param name="Omitted">Lines left out in the middle of a long log (the first and the last ones are kept).</param>
public sealed record TaskRunDetails(
    long Id,
    BackgroundTaskKind Task,
    string? Path,
    TaskTrigger Trigger,
    int? ScheduleId,
    DateTimeOffset Started,
    DateTimeOffset? Finished,
    TaskOutcome Outcome,
    string? Summary,
    IReadOnlyList<TaskLogLine> Log,
    int Omitted)
    : TaskRunSummary(Id, Task, Path, Trigger, ScheduleId, Started, Finished, Outcome, Summary);

/// <param name="Next">Value of <c>before</c> that requests the next (older) page; null on the last one.</param>
public sealed record TaskRunPage(IReadOnlyList<TaskRunSummary> Runs, long? Next);

/// <summary>A schedule as sent and received by the API.</summary>
/// <param name="Path">Folder to scan, relative to the library root (empty: the whole library); scans only.</param>
/// <param name="Start">First run (<see cref="ScheduleKind.Once"/>, <see cref="ScheduleKind.Interval"/>), or the time of day
/// (server time) and the first day it can run (<see cref="ScheduleKind.Weekly"/>).</param>
/// <param name="IntervalMinutes">Time between two runs, for <see cref="ScheduleKind.Interval"/>.</param>
/// <param name="Days">Days of <see cref="ScheduleKind.Weekly"/>: bit 0 = Sunday … bit 6 = Saturday.</param>
public record TaskScheduleInput(
    BackgroundTaskKind Task,
    string? Path,
    ScheduleKind Kind,
    DateTimeOffset Start,
    int IntervalMinutes,
    int Days,
    bool Enabled);

/// <param name="NextRun">When it runs next; null when it is off or a one-off that has run (or was missed).</param>
/// <param name="LastRun">When it last started its task.</param>
public sealed record TaskScheduleView(
    int Id,
    BackgroundTaskKind Task,
    string? Path,
    ScheduleKind Kind,
    DateTimeOffset Start,
    int IntervalMinutes,
    int Days,
    bool Enabled,
    DateTimeOffset? NextRun,
    DateTimeOffset? LastRun)
    : TaskScheduleInput(Task, Path, Kind, Start, IntervalMinutes, Days, Enabled);

/// <summary>Everything the "Background tasks" dialog shows, refreshed while it is open.</summary>
/// <param name="TimeZone">The server's time zone (IANA id when known): weekly schedules follow its clock.</param>
/// <param name="LogRetentionDays">Days run logs are kept (<c>Tasks:LogRetentionDays</c>).</param>
public sealed record TaskOverview(
    IReadOnlyList<TaskInfo> Tasks,
    IReadOnlyList<TaskScheduleView> Schedules,
    DateTimeOffset ServerTime,
    string TimeZone,
    int LogRetentionDays);
