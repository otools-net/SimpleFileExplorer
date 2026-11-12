using Sfe.Core.Tasks;

namespace Sfe.Api.Endpoints;

/// <param name="Result">What happened: started, already running (a trickplay pass then follows the current one), or
/// unavailable.</param>
/// <param name="Task">The task's state afterwards.</param>
public sealed record TaskActionResult(TaskStartResult Result, TaskInfo Task);

/// <param name="Runs">The next occurrences (at most five), in UTC.</param>
public sealed record SchedulePreview(IReadOnlyList<DateTimeOffset> Runs);
