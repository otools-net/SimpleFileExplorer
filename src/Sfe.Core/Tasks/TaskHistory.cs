using System.Collections.Concurrent;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Sfe.Core.Data;
using Sfe.Core.Options;

namespace Sfe.Core.Tasks;

/// <summary>
/// Records the runs of the background tasks in <c>TaskRuns</c>, with their logs, for the "Background tasks" dialog's
/// history, and forgets them after <see cref="TaskOptions.LogRetentionDays"/>. A run is written when it starts (so a
/// server stopping in the middle leaves it to <see cref="RecoverAsync"/>), and its log when it ends; until then the
/// log lives in memory. The tasks work without the index: when the database can't be written, runs are only kept in
/// memory while they run.
/// </summary>
public sealed class TaskHistory(IDbContextFactory<SfeDbContext> dbFactory, IOptionsMonitor<SfeOptions> options, ILogger<TaskHistory> logger)
{
    public const int DefaultPageSize = 50;
    public const int MaxPageSize = 500;
    private const int MaxSummaryLength = 1000;

    private readonly ConcurrentDictionary<long, Run> _active = new();
    private readonly ConcurrentDictionary<BackgroundTaskKind, TaskRunSummary> _last = new();
    private volatile bool _lastLoaded;
    private long _memoryIds;

    /// <summary>
    /// Starts recording a run: from now on, what the calling async flow logs goes into its log. Call it from the
    /// run's own method (see <see cref="TaskLogProvider.Current"/>), and end the run with <see cref="Run.CompleteAsync"/>.
    /// </summary>
    public Run Begin(BackgroundTaskKind task, string? path, TaskTrigger trigger, int? scheduleId = null)
    {
        var started = DateTime.UtcNow;
        var id = Insert(new TaskRun { Task = task, Path = path, Trigger = trigger, ScheduleId = scheduleId, Started = started, Outcome = TaskOutcome.Running });
        var run = new Run(this, id, task, path, trigger, scheduleId, started);
        _active[id] = run;
        _last[task] = run.Summary(TaskOutcome.Running, null, null);
        TaskLogProvider.Current = run.Log;
        return run;
    }

    /// <summary>Records a schedule that came due while its task could not start.</summary>
    public async Task RecordSkippedAsync(BackgroundTaskKind task, string? path, int scheduleId, string reason, CancellationToken cancellationToken = default)
    {
        var now = DateTime.UtcNow;
        var row = new TaskRun
        {
            Task = task, Path = path, Trigger = TaskTrigger.Schedule, ScheduleId = scheduleId,
            Started = now, Finished = now, Outcome = TaskOutcome.Skipped, Summary = Truncate(reason),
        };
        try
        {
            await using var db = await dbFactory.CreateDbContextAsync(cancellationToken);
            db.TaskRuns.Add(row);
            await db.SaveChangesAsync(cancellationToken);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            logger.LogWarning(ex, "Cannot record the skipped run of {Task}", task);
        }
    }

    /// <summary>Runs newest first, of one task or all; <paramref name="before"/>: <see cref="TaskRunPage.Next"/> of the previous page.</summary>
    public async Task<TaskRunPage> ListAsync(BackgroundTaskKind? task, long? before, int? limit, CancellationToken cancellationToken = default)
    {
        var size = Math.Clamp(limit ?? DefaultPageSize, 1, MaxPageSize);
        await using var db = await dbFactory.CreateDbContextAsync(cancellationToken);
        var query = db.TaskRuns.AsNoTracking();
        if (task is { } kind)
            query = query.Where(r => r.Task == kind);
        if (before is { } id)
            query = query.Where(r => r.Id < id);
        var rows = await query
            .OrderByDescending(r => r.Id)
            .Take(size + 1)
            .Select(r => new { r.Id, r.Task, r.Path, r.Trigger, r.ScheduleId, r.Started, r.Finished, r.Outcome, r.Summary })
            .ToListAsync(cancellationToken);
        var runs = rows.Take(size)
            .Select(r => new TaskRunSummary(r.Id, r.Task, r.Path, r.Trigger, r.ScheduleId, Utc(r.Started), Utc(r.Finished), r.Outcome, r.Summary))
            .ToList();
        return new TaskRunPage(runs, rows.Count > size ? runs[^1].Id : null);
    }

    /// <summary>A run with its log: live while it runs.</summary>
    public async Task<TaskRunDetails?> GetAsync(long id, CancellationToken cancellationToken = default)
    {
        if (_active.TryGetValue(id, out var active))
            return active.Details();
        if (id <= 0)
            return null;

        await using var db = await dbFactory.CreateDbContextAsync(cancellationToken);
        var row = await db.TaskRuns.AsNoTracking().FirstOrDefaultAsync(r => r.Id == id, cancellationToken);
        if (row is null)
            return null;
        var (lines, omitted) = TaskRunLog.Deserialize(row.Log);
        return new TaskRunDetails(row.Id, row.Task, row.Path, row.Trigger, row.ScheduleId, Utc(row.Started), Utc(row.Finished), row.Outcome, row.Summary, lines, omitted);
    }

    /// <summary>The last run of each task that ran (or runs), for the task list.</summary>
    public async Task<IReadOnlyDictionary<BackgroundTaskKind, TaskRunSummary>> LastRunsAsync(CancellationToken cancellationToken = default)
    {
        if (!_lastLoaded)
        {
            try
            {
                await using var db = await dbFactory.CreateDbContextAsync(cancellationToken);
                foreach (var kind in Enum.GetValues<BackgroundTaskKind>())
                {
                    var row = await db.TaskRuns.AsNoTracking()
                        .Where(r => r.Task == kind && r.Outcome != TaskOutcome.Skipped)
                        .OrderByDescending(r => r.Id)
                        .FirstOrDefaultAsync(cancellationToken);
                    if (row is not null)
                        _last.TryAdd(kind, new TaskRunSummary(row.Id, row.Task, row.Path, row.Trigger, row.ScheduleId, Utc(row.Started), Utc(row.Finished), row.Outcome, row.Summary));
                }

                _lastLoaded = true;
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                logger.LogDebug(ex, "Cannot read the last task runs");
            }
        }

        return new Dictionary<BackgroundTaskKind, TaskRunSummary>(_last);
    }

    /// <summary>Deletes the finished runs; returns how many.</summary>
    public async Task<int> ClearAsync(CancellationToken cancellationToken = default)
    {
        await using var db = await dbFactory.CreateDbContextAsync(cancellationToken);
        var deleted = await db.TaskRuns.Where(r => r.Outcome != TaskOutcome.Running).ExecuteDeleteAsync(cancellationToken);
        foreach (var (kind, last) in _last)
        {
            if (last.Outcome != TaskOutcome.Running)
                _last.TryRemove(kind, out _);
        }

        return deleted;
    }

    /// <summary>Forgets the runs that finished more than <see cref="TaskOptions.LogRetentionDays"/> ago.</summary>
    public async Task PurgeAsync(CancellationToken cancellationToken = default)
    {
        var days = options.CurrentValue.Tasks.LogRetentionDays;
        if (days <= 0)
            return;
        try
        {
            var limit = DateTime.UtcNow.AddDays(-days);
            await using var db = await dbFactory.CreateDbContextAsync(cancellationToken);
            var deleted = await db.TaskRuns.Where(r => r.Finished != null && r.Finished < limit).ExecuteDeleteAsync(cancellationToken);
            if (deleted > 0)
                logger.LogInformation("Forgot {Count} task runs older than {Days} days", deleted, days);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            logger.LogWarning(ex, "Cannot purge the task history");
        }
    }

    /// <summary>At start-up: runs still marked running were cut short by the server stopping.</summary>
    public async Task RecoverAsync(CancellationToken cancellationToken = default)
    {
        try
        {
            await using var db = await dbFactory.CreateDbContextAsync(cancellationToken);
            var active = _active.Keys.ToList();
            await db.TaskRuns
                .Where(r => r.Outcome == TaskOutcome.Running && !active.Contains(r.Id))
                .ExecuteUpdateAsync(s => s
                    .SetProperty(r => r.Outcome, TaskOutcome.Interrupted)
                    .SetProperty(r => r.Finished, r => r.Started)
                    .SetProperty(r => r.Summary, "The server stopped while it ran."), cancellationToken);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            logger.LogWarning(ex, "Cannot read the task history: is the database initialized?");
        }
    }

    private long Insert(TaskRun row)
    {
        try
        {
            using var db = dbFactory.CreateDbContext();
            db.TaskRuns.Add(row);
            db.SaveChanges();
            return row.Id;
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Cannot record the run of {Task}: it is only shown while it runs", row.Task);
            return -Interlocked.Increment(ref _memoryIds);
        }
    }

    private async Task CompleteAsync(Run run, TaskOutcome outcome, string? summary)
    {
        var finished = DateTime.UtcNow;
        summary = summary is null ? null : Truncate(summary);
        if (run.Id > 0)
        {
            try
            {
                await using var db = await dbFactory.CreateDbContextAsync();
                await db.TaskRuns.Where(r => r.Id == run.Id).ExecuteUpdateAsync(s => s
                    .SetProperty(r => r.Finished, finished)
                    .SetProperty(r => r.Outcome, outcome)
                    .SetProperty(r => r.Summary, summary)
                    .SetProperty(r => r.Log, run.Log.Serialize()));
            }
            catch (Exception ex)
            {
                logger.LogWarning(ex, "Cannot record the end of the run of {Task}", run.Task);
            }
        }

        _last[run.Task] = run.Summary(outcome, summary, finished);
        _active.TryRemove(run.Id, out _);
        await PurgeAsync();
    }

    private static string Truncate(string text) => text.Length > MaxSummaryLength ? text[..MaxSummaryLength] + "…" : text;

    private static DateTimeOffset Utc(DateTime value) => new(DateTime.SpecifyKind(value, DateTimeKind.Utc));

    private static DateTimeOffset? Utc(DateTime? value) => value is { } v ? Utc(v) : null;

    /// <summary>A run in progress.</summary>
    public sealed class Run
    {
        private readonly TaskHistory _history;
        private int _completed;

        internal Run(TaskHistory history, long id, BackgroundTaskKind task, string? path, TaskTrigger trigger, int? scheduleId, DateTime started)
        {
            _history = history;
            Id = id;
            Task = task;
            Path = path;
            Trigger = trigger;
            ScheduleId = scheduleId;
            Started = started;
        }

        /// <summary>Row id; negative when the run could not be written (kept in memory while it runs).</summary>
        public long Id { get; }

        public BackgroundTaskKind Task { get; }
        public string? Path { get; }
        public TaskTrigger Trigger { get; }
        public int? ScheduleId { get; }
        public DateTime Started { get; }
        public TaskRunLog Log { get; } = new();

        /// <summary>Ends the run (once) and stops collecting the flow's log entries.</summary>
        public Task CompleteAsync(TaskOutcome outcome, string? summary)
        {
            if (TaskLogProvider.Current == Log)
                TaskLogProvider.Current = null;
            return Interlocked.Exchange(ref _completed, 1) == 0 ? _history.CompleteAsync(this, outcome, summary) : System.Threading.Tasks.Task.CompletedTask;
        }

        internal TaskRunSummary Summary(TaskOutcome outcome, string? summary, DateTime? finished) =>
            new(Id, Task, Path, Trigger, ScheduleId, Utc(Started), Utc(finished), outcome, summary);

        internal TaskRunDetails Details()
        {
            var (lines, omitted) = Log.Snapshot();
            return new TaskRunDetails(Id, Task, Path, Trigger, ScheduleId, Utc(Started), null, TaskOutcome.Running, null, lines, omitted);
        }
    }
}
