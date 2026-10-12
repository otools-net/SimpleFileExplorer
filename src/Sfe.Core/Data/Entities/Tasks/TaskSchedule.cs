namespace Sfe.Core.Data;

/// <summary>
/// When to start a background task (see <see cref="Tasks.TaskScheduleService"/>). Times are UTC; weekly schedules run
/// at the time of day <see cref="Start"/> has in the server's time zone.
/// </summary>
public sealed class TaskSchedule
{
    public int Id { get; set; }
    public Tasks.BackgroundTaskKind Task { get; set; }

    /// <summary>Folder to scan, relative to the library root; null for the whole library and for other tasks.</summary>
    public string? Path { get; set; }

    public Tasks.ScheduleKind Kind { get; set; }
    public DateTime Start { get; set; }
    public int IntervalMinutes { get; set; }

    /// <summary>Days of a weekly schedule: bit 0 = Sunday … bit 6 = Saturday.</summary>
    public int Days { get; set; }

    public bool Enabled { get; set; } = true;

    /// <summary>Created or last changed: occurrences before it are not due (a new schedule doesn't run for the past).</summary>
    public DateTime Updated { get; set; }

    /// <summary>Last time it came due (whether its task could start or not).</summary>
    public DateTime? LastRun { get; set; }
}
