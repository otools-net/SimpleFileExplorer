namespace Otools.Net.Sfe.Core.Tasks;

/// <param name="Run">The run as it ended.</param>
/// <param name="StartedBy">The account that started it (<see cref="TaskStarter"/>); null when a schedule or the task
/// itself did, or a visitor.</param>
public sealed record TaskEnding(TaskRunSummary Run, int? StartedBy);
