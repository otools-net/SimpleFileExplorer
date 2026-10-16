using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.Extensions.Options;
using Sfe.Api.Security;
using Sfe.Core.Access;
using Sfe.Core.Options;
using Sfe.Core.Paths;
using Sfe.Core.Tasks;

namespace Sfe.Api.Endpoints;

/// <param name="Result">What happened: started, already running (a trickplay pass then follows the current one), or unavailable.</param>
/// <param name="Task">The task's state afterwards.</param>
public sealed record TaskActionResult(TaskStartResult Result, TaskInfo Task);

/// <param name="Runs">The next occurrences (at most five), in UTC.</param>
public sealed record SchedulePreview(IReadOnlyList<DateTimeOffset> Runs);

public static class TaskEndpoints
{
    public static RouteGroupBuilder MapTaskEndpoints(this RouteGroupBuilder api)
    {
        var tasks = api.MapGroup("/tasks").WithTags("Tasks");

        tasks.MapGet("/", Overview)
            .RequireRight(Rights.ViewTasks)
            .WithName("GetTasks")
            .WithSummary("The background tasks with their state and last run, the schedules with their next run.")
            .WithDescription("""
                Tasks: `scan`, `trickplay`, `similarVideos`, `thumbnails`. `automaticNextRun` is when a task runs by
                itself next (trickplay's periodic check), whatever the schedules. Weekly schedules follow the clock of
                the server's `timeZone`.
                """);

        tasks.MapPost("/{task}/start", Start)
            .RequireRight(Rights.RunTasks)
            .FolderScoped("a task on a folder (a scan) needs index.scan there")
            .WithName("StartTask")
            .WithSummary("Starts a task now (thumbnails can't be started: listings ask for them).")
            .WithDescription("A scan covers `path` (relative to the library root; empty: the whole library). 409 when it already runs, or is turned off; a trickplay pass asked for while one runs follows it (202).");

        tasks.MapPost("/{task}/cancel", Cancel)
            .RequireRight(Rights.RunTasks)
            .WithName("CancelTask")
            .WithSummary("Stops a running task.")
            .WithDescription("""
                A scan keeps what it indexed so far. Trickplay stops its pass and the player's queue (the periodic
                passes go on). Thumbnails drops the renders running and waiting. 409 when it wasn't running.
                """);

        tasks.MapPost("/schedules", CreateSchedule)
            .RequireRight(Rights.ScheduleTasks)
            .FolderScoped("a schedule's folder must be one the subject sees")
            .WithName("CreateTaskSchedule")
            .WithSummary("Schedules a task: once, every N minutes from a start, or on days of the week at a time.");

        tasks.MapPut("/schedules/{id:int}", UpdateSchedule)
            .RequireRight(Rights.ScheduleTasks)
            .FolderScoped("a schedule's folder must be one the subject sees")
            .WithName("UpdateTaskSchedule")
            .WithSummary("Changes a schedule; its next run is worked out again from now.");

        tasks.MapDelete("/schedules/{id:int}", DeleteSchedule)
            .RequireRight(Rights.ScheduleTasks)
            .WithName("DeleteTaskSchedule")
            .WithSummary("Deletes a schedule; its past runs stay in the history.");

        tasks.MapPost("/schedules/preview", PreviewSchedule)
            .FolderScoped("only works out run times: the folder is checked when the schedule is saved")
            .RequireRight(Rights.ScheduleTasks)
            .WithName("PreviewTaskSchedule")
            .WithSummary("The next runs of a schedule being edited, or its validation errors.");

        tasks.MapGet("/runs", Runs)
            .RequireRight(Rights.ViewTasks)
            .WithName("GetTaskRuns")
            .WithSummary("Runs of the background tasks, newest first, without their logs.")
            .WithDescription($"Optionally of one `task`. Pages of `limit` runs (default {TaskHistory.DefaultPageSize}, at most {TaskHistory.MaxPageSize}); pass `next` as `before` for the following page.");

        tasks.MapGet("/runs/{id:long}", Run)
            .RequireRight(Rights.ViewTasks)
            .WithName("GetTaskRun")
            .WithSummary("A run with its log (live while it runs).");

        tasks.MapDelete("/runs", ClearRuns)
            .RequireRight(Rights.ScheduleTasks)
            .WithName("ClearTaskRuns")
            .WithSummary("Deletes the history of finished runs.");

        return api;
    }

    private static async Task<Ok<TaskOverview>> Overview(
        BackgroundTasks tasks, TaskScheduleService schedules, IOptionsMonitor<SfeOptions> options, CancellationToken cancellationToken) =>
        TypedResults.Ok(new TaskOverview(
            await tasks.DescribeAsync(cancellationToken),
            await schedules.ListAsync(cancellationToken),
            DateTimeOffset.Now,
            schedules.TimeZoneId,
            options.CurrentValue.Tasks.LogRetentionDays));

    private static async Task<Results<Accepted<TaskActionResult>, Conflict<TaskActionResult>, NotFound>> Start(
        BackgroundTasks tasks, LibraryPaths paths, CurrentAccess current, string task, string? path, CancellationToken cancellationToken)
    {
        if (!TryParseTask(task, out var kind))
            return TypedResults.NotFound();
        // A task on a folder (a scan) runs only where the subject may scan.
        if (!string.IsNullOrEmpty(path) && (await current.GetAsync()).Check(paths, path, Rights.Scan) != PathVerdict.Allowed)
            return TypedResults.NotFound();
        var result = tasks.Start(kind, path, TaskTrigger.Manual);
        var body = new TaskActionResult(result, await DescribeAsync(tasks, kind, cancellationToken));
        return result == TaskStartResult.Started || (result == TaskStartResult.AlreadyRunning && kind == BackgroundTaskKind.Trickplay)
            ? TypedResults.Accepted("/api/tasks", body)
            : TypedResults.Conflict(body);
    }

    private static async Task<Results<Accepted<TaskInfo>, Conflict<TaskInfo>, NotFound>> Cancel(BackgroundTasks tasks, string task, CancellationToken cancellationToken)
    {
        if (!TryParseTask(task, out var kind))
            return TypedResults.NotFound();
        var cancelled = tasks.Cancel(kind);
        var info = await DescribeAsync(tasks, kind, cancellationToken);
        return cancelled ? TypedResults.Accepted("/api/tasks", info) : TypedResults.Conflict(info);
    }

    /// <summary>A task by its name, in any case (the API sends camelCase); numbers are refused.</summary>
    private static bool TryParseTask(string name, out BackgroundTaskKind task) =>
        Enum.TryParse(name, ignoreCase: true, out task) && Enum.IsDefined(task) && !char.IsDigit(name[0]);

    private static async Task<TaskInfo> DescribeAsync(BackgroundTasks tasks, BackgroundTaskKind task, CancellationToken cancellationToken) =>
        (await tasks.DescribeAsync(cancellationToken)).First(t => t.Task == task);

    private static async Task<Results<Created<TaskScheduleView>, ValidationProblem>> CreateSchedule(
        TaskScheduleService schedules, LibraryPaths paths, CurrentAccess current, TaskScheduleInput input, CancellationToken cancellationToken)
    {
        if (await UnseenFolder(paths, current, input.Path) is { } unseen)
            return unseen;
        var (view, errors) = await schedules.CreateAsync(input, cancellationToken);
        return view is null ? TypedResults.ValidationProblem(errors!) : TypedResults.Created($"/api/tasks/schedules/{view.Id}", view);
    }

    private static async Task<Results<Ok<TaskScheduleView>, NotFound, ValidationProblem>> UpdateSchedule(
        TaskScheduleService schedules, LibraryPaths paths, CurrentAccess current, int id, TaskScheduleInput input, CancellationToken cancellationToken)
    {
        if (await UnseenFolder(paths, current, input.Path) is { } unseen)
            return unseen;
        var (found, view, errors) = await schedules.UpdateAsync(id, input, cancellationToken);
        return !found ? TypedResults.NotFound()
            : view is null ? TypedResults.ValidationProblem(errors!)
            : TypedResults.Ok(view);
    }

    /// <summary>A schedule's folder must be one the subject sees: answered as an invalid folder otherwise.</summary>
    private static async Task<ValidationProblem?> UnseenFolder(LibraryPaths paths, CurrentAccess current, string? path) =>
        string.IsNullOrEmpty(path) || (await current.GetAsync()).At(paths, path) is { IsVisible: true }
            ? null
            : TypedResults.ValidationProblem(new Dictionary<string, string[]> { ["path"] = ["Invalid library path."] });

    private static async Task<Results<NoContent, NotFound>> DeleteSchedule(TaskScheduleService schedules, int id, CancellationToken cancellationToken) =>
        await schedules.DeleteAsync(id, cancellationToken) ? TypedResults.NoContent() : TypedResults.NotFound();

    private static Results<Ok<SchedulePreview>, ValidationProblem> PreviewSchedule(TaskScheduleService schedules, TaskScheduleInput input)
    {
        var (runs, errors) = schedules.Preview(input);
        return runs is null ? TypedResults.ValidationProblem(errors!) : TypedResults.Ok(new SchedulePreview(runs));
    }

    private static async Task<Results<Ok<TaskRunPage>, NotFound>> Runs(
        TaskHistory history, string? task, long? before, int? limit, CancellationToken cancellationToken)
    {
        BackgroundTaskKind? kind = null;
        if (!string.IsNullOrEmpty(task))
        {
            if (!TryParseTask(task, out var parsed))
                return TypedResults.NotFound();
            kind = parsed;
        }

        return TypedResults.Ok(await history.ListAsync(kind, before, limit, cancellationToken));
    }

    private static async Task<Results<Ok<TaskRunDetails>, NotFound>> Run(TaskHistory history, long id, CancellationToken cancellationToken) =>
        await history.GetAsync(id, cancellationToken) is { } run ? TypedResults.Ok(run) : TypedResults.NotFound();

    private static async Task<NoContent> ClearRuns(TaskHistory history, CancellationToken cancellationToken)
    {
        await history.ClearAsync(cancellationToken);
        return TypedResults.NoContent();
    }
}
