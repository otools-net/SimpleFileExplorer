using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Sfe.Core.Data;
using Sfe.Core.Paths;

namespace Sfe.Core.Tasks;

/// <summary>
/// The schedules of the "Background tasks" dialog (table <c>TaskSchedules</c>), and the loop that starts their tasks
/// when they come due (<see cref="RunAsync"/>, run by the API's hosted service).
/// </summary>
/// <remarks>
/// Occurrences missed while the server was off are skipped, not caught up: a scan of the library at 03:00 that
/// didn't happen runs at 03:00 the next day, not at start-up. Only one run is started when several occurrences of a
/// schedule came due at once. A schedule coming due while its task already runs is recorded as skipped.
/// </remarks>
public sealed class TaskScheduleService(
    IDbContextFactory<SfeDbContext> dbFactory,
    BackgroundTasks tasks,
    TaskHistory history,
    ILogger<TaskScheduleService> logger) : IDisposable
{
    public const int MaxSchedules = 100;

    /// <summary>A year: a longer interval is a typo.</summary>
    public const int MaxIntervalMinutes = 366 * 24 * 60;

    /// <summary>Occurrences listed by <see cref="Preview"/>.</summary>
    public const int PreviewCount = 5;

    /// <summary>The loop looks at the schedules at least this often (a changed clock, a database back online).</summary>
    private static readonly TimeSpan MaxWait = TimeSpan.FromMinutes(30);
    private static readonly TimeSpan PurgeEvery = TimeSpan.FromHours(6);

    private readonly SemaphoreSlim _changed = new(0);

    /// <summary>Occurrences up to this moment were handled by the loop.</summary>
    private DateTime _lastCheck = DateTime.UtcNow;

    /// <summary>Clock of the weekly schedules; the server's, replaced by tests.</summary>
    internal TimeZoneInfo Zone { get; set; } = TimeZoneInfo.Local;

    /// <summary>The server's time zone, as an IANA id (like the browsers') when there is one.</summary>
    public string TimeZoneId => Zone.HasIanaId ? Zone.Id
        : TimeZoneInfo.TryConvertWindowsIdToIanaId(Zone.Id, out var iana) ? iana
        : Zone.Id;

    public async Task<List<TaskScheduleView>> ListAsync(CancellationToken cancellationToken = default)
    {
        await using var db = await dbFactory.CreateDbContextAsync(cancellationToken);
        var schedules = await db.TaskSchedules.AsNoTracking().OrderBy(s => s.Id).ToListAsync(cancellationToken);
        var now = DateTime.UtcNow;
        return [.. schedules.Select(s => View(s, now))];
    }

    public async Task<(TaskScheduleView? View, Dictionary<string, string[]>? Errors)> CreateAsync(TaskScheduleInput input, CancellationToken cancellationToken = default)
    {
        var now = DateTime.UtcNow;
        var (schedule, errors) = Validate(input, null, now);
        if (errors.Count > 0)
            return (null, errors);

        await using var db = await dbFactory.CreateDbContextAsync(cancellationToken);
        if (await db.TaskSchedules.CountAsync(cancellationToken) >= MaxSchedules)
            return (null, new() { ["schedule"] = [$"At most {MaxSchedules} schedules."] });

        schedule!.Updated = now;
        db.TaskSchedules.Add(schedule);
        await db.SaveChangesAsync(cancellationToken);
        logger.LogInformation("Schedule {Id} created: {Task} {Kind}", schedule.Id, schedule.Task, schedule.Kind);
        _changed.Release();
        return (View(schedule, now), null);
    }

    /// <returns>Found false when there is no such schedule.</returns>
    public async Task<(bool Found, TaskScheduleView? View, Dictionary<string, string[]>? Errors)> UpdateAsync(int id, TaskScheduleInput input, CancellationToken cancellationToken = default)
    {
        await using var db = await dbFactory.CreateDbContextAsync(cancellationToken);
        var schedule = await db.TaskSchedules.FirstOrDefaultAsync(s => s.Id == id, cancellationToken);
        if (schedule is null)
            return (false, null, null);

        var now = DateTime.UtcNow;
        var (changed, errors) = Validate(input, schedule, now);
        if (errors.Count > 0)
            return (true, null, errors);

        schedule.Task = changed!.Task;
        schedule.Path = changed.Path;
        schedule.Kind = changed.Kind;
        schedule.Start = changed.Start;
        schedule.IntervalMinutes = changed.IntervalMinutes;
        schedule.Days = changed.Days;
        schedule.Enabled = changed.Enabled;
        schedule.Updated = now;
        await db.SaveChangesAsync(cancellationToken);
        _changed.Release();
        return (true, View(schedule, now), null);
    }

    public async Task<bool> DeleteAsync(int id, CancellationToken cancellationToken = default)
    {
        await using var db = await dbFactory.CreateDbContextAsync(cancellationToken);
        var deleted = await db.TaskSchedules.Where(s => s.Id == id).ExecuteDeleteAsync(cancellationToken) > 0;
        if (deleted)
            _changed.Release();
        return deleted;
    }

    /// <summary>The first <see cref="PreviewCount"/> runs of a schedule being edited, or why it is invalid.</summary>
    public (IReadOnlyList<DateTimeOffset>? Runs, Dictionary<string, string[]>? Errors) Preview(TaskScheduleInput input)
    {
        var now = DateTime.UtcNow;
        var (schedule, errors) = Validate(input, null, now, requireFuture: false);
        if (errors.Count > 0)
            return (null, errors);
        var runs = ScheduleCalculator.NextRuns(schedule!.Kind, schedule.Start, schedule.IntervalMinutes, schedule.Days, now, Zone, PreviewCount);
        return ([.. runs.Select(Utc)], null);
    }

    /// <summary>Starts the tasks of the schedules as they come due, until <paramref name="stoppingToken"/> is cancelled.</summary>
    public async Task RunAsync(CancellationToken stoppingToken)
    {
        await history.RecoverAsync(stoppingToken);
        await history.PurgeAsync(stoppingToken);
        var lastPurge = DateTime.UtcNow;
        _lastCheck = DateTime.UtcNow;
        while (!stoppingToken.IsCancellationRequested)
        {
            var wait = MaxWait;
            try
            {
                wait = await CheckAsync(stoppingToken);
                if (DateTime.UtcNow - lastPurge > PurgeEvery)
                {
                    lastPurge = DateTime.UtcNow;
                    await history.PurgeAsync(stoppingToken);
                }
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                return;
            }
            catch (Exception ex)
            {
                logger.LogWarning(ex, "Cannot read the task schedules");
            }

            try
            {
                // Woken up early by a schedule being created, changed or deleted.
                if (wait > TimeSpan.Zero)
                    await _changed.WaitAsync(wait, stoppingToken);
            }
            catch (OperationCanceledException)
            {
                return;
            }
        }
    }

    /// <summary>Starts what came due since the last check; returns the time until the next occurrence.</summary>
    private async Task<TimeSpan> CheckAsync(CancellationToken cancellationToken)
    {
        var now = DateTime.UtcNow;
        await using var db = await dbFactory.CreateDbContextAsync(cancellationToken);
        var schedules = await db.TaskSchedules.Where(s => s.Enabled).ToListAsync(cancellationToken);
        foreach (var schedule in schedules)
        {
            // Not before it was created or changed: a new schedule doesn't run for an occurrence already past.
            var after = Latest(_lastCheck, schedule.Updated, schedule.LastRun);
            if (Next(schedule, after) is { } due && due <= now)
            {
                schedule.LastRun = now;
                await StartAsync(schedule, cancellationToken);
            }
        }

        await db.SaveChangesAsync(cancellationToken);
        _lastCheck = now;

        var next = schedules.Select(s => Next(s, Latest(now, s.Updated, s.LastRun))).Where(d => d is not null).Min();
        if (next is null)
            return MaxWait;
        // A little after, so that the occurrence is past when the loop wakes up.
        var wait = next.Value - DateTime.UtcNow + TimeSpan.FromMilliseconds(20);
        return wait < MaxWait ? wait : MaxWait;
    }

    private async Task StartAsync(TaskSchedule schedule, CancellationToken cancellationToken)
    {
        var result = tasks.Start(schedule.Task, schedule.Path, TaskTrigger.Schedule, schedule.Id);
        logger.LogInformation("Schedule {Id} came due: {Task} {Result}", schedule.Id, schedule.Task, result);
        // A trickplay pass asked for while one runs starts when it ends.
        if (result == TaskStartResult.Started || (result == TaskStartResult.AlreadyRunning && schedule.Task == BackgroundTaskKind.Trickplay))
            return;

        var reason = result == TaskStartResult.AlreadyRunning
            ? "Skipped: the task was already running."
            : "Skipped: the task is turned off or ffmpeg can't be found.";
        await history.RecordSkippedAsync(schedule.Task, schedule.Path ?? string.Empty, schedule.Id, reason, cancellationToken);
    }

    private DateTime? Next(TaskSchedule s, DateTime afterUtc) =>
        ScheduleCalculator.Next(s.Kind, s.Start, s.IntervalMinutes, s.Days, afterUtc, Zone);

    private TaskScheduleView View(TaskSchedule s, DateTime now) => new(
        s.Id, s.Task, s.Path, s.Kind, Utc(s.Start), s.IntervalMinutes, s.Days, s.Enabled,
        s.Enabled && Next(s, Latest(now, now, s.LastRun)) is { } next ? Utc(next) : null,
        s.LastRun is { } last ? Utc(last) : null);

    /// <summary>
    /// The schedule <paramref name="input"/> describes, normalized: fields its kind and task don't use are cleared.
    /// </summary>
    /// <param name="existing">The schedule being changed; a one-off whose time is kept needn't be in the future.</param>
    /// <param name="requireFuture">A one-off must not be in the past (unless off).</param>
    private static (TaskSchedule? Schedule, Dictionary<string, string[]> Errors) Validate(
        TaskScheduleInput input, TaskSchedule? existing, DateTime now, bool requireFuture = true)
    {
        var errors = new Dictionary<string, string[]>();
        void Check(bool valid, string key, string message)
        {
            if (!valid && !errors.ContainsKey(key))
                errors[key] = [message];
        }

        Check(Enum.IsDefined(input.Task) && BackgroundTasks.CanSchedule(input.Task), "task", "This task can't be scheduled.");
        Check(Enum.IsDefined(input.Kind), "kind", "Choose when the task runs.");
        string? path = null;
        if (input.Task == BackgroundTaskKind.Scan)
        {
            path = LibraryPaths.NormalizeRelative(input.Path);
            Check(path is not null, "path", "Enter a folder of the library, like Movies/Series, or leave it empty for the whole library.");
        }

        var start = input.Start.UtcDateTime;
        if (input.Kind == ScheduleKind.Interval)
            Check(input.IntervalMinutes is >= 1 and <= MaxIntervalMinutes, "intervalMinutes", $"The interval must be between 1 minute and {MaxIntervalMinutes / (24 * 60)} days.");
        if (input.Kind == ScheduleKind.Weekly)
            Check((input.Days & ScheduleCalculator.AllDays) != 0, "days", "Choose at least one day.");
        // A minute's grace: the time picked in the form may have just passed.
        var keepsTime = existing is not null && existing.Kind == ScheduleKind.Once && existing.Start == start;
        if (input.Kind == ScheduleKind.Once && input.Enabled && requireFuture && !keepsTime)
            Check(start > now.AddMinutes(-1), "start", "Choose a time in the future.");

        if (errors.Count > 0)
            return (null, errors);

        return (new TaskSchedule
        {
            Task = input.Task,
            Path = string.IsNullOrEmpty(path) ? null : path,
            Kind = input.Kind,
            Start = start,
            IntervalMinutes = input.Kind == ScheduleKind.Interval ? input.IntervalMinutes : 0,
            Days = input.Kind == ScheduleKind.Weekly ? input.Days & ScheduleCalculator.AllDays : 0,
            Enabled = input.Enabled,
        }, errors);
    }

    private static DateTime Latest(DateTime first, DateTime second, DateTime? third = null)
    {
        var latest = first > second ? first : second;
        return third is { } t && t > latest ? t : latest;
    }

    private static DateTimeOffset Utc(DateTime value) => new(DateTime.SpecifyKind(value, DateTimeKind.Utc));

    public void Dispose() => _changed.Dispose();
}
