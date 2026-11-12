namespace Sfe.Core.Options;

/// <summary>The background tasks (scans, trickplay, similar videos): their run history.</summary>
public sealed class TaskOptions
{
    /// <summary>Days the runs of the background tasks are kept, with their logs; 0 = forever.</summary>
    public int LogRetentionDays { get; set; } = 30;
}
