namespace Sfe.Core.Data;

/// <summary>A run of a background task, with its log, kept <c>Tasks:LogRetentionDays</c> (see <see cref="Tasks.TaskHistory"/>).</summary>
public sealed class TaskRun
{
    public long Id { get; set; }
    public Tasks.BackgroundTaskKind Task { get; set; }

    /// <summary>Folder a scan covered, relative to the library root.</summary>
    public string? Path { get; set; }

    public Tasks.TaskTrigger Trigger { get; set; }

    /// <summary>Schedule that started it; not a foreign key, the run outlives its schedule.</summary>
    public int? ScheduleId { get; set; }

    /// <summary>UTC.</summary>
    public DateTime Started { get; set; }

    /// <summary>UTC; null while it runs.</summary>
    public DateTime? Finished { get; set; }

    public Tasks.TaskOutcome Outcome { get; set; }
    public string? Summary { get; set; }

    /// <summary>The log lines as JSON (<see cref="Tasks.TaskRunLog"/>).</summary>
    public string? Log { get; set; }
}
