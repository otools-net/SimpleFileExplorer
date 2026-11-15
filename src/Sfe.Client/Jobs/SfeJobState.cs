namespace Otools.Net.Sfe.Client;

/// <summary>Where a job stands.</summary>
public enum SfeJobState
{
    /// <summary>A state this version of the library doesn't know (a newer server's).</summary>
    Unknown,

    /// <summary>Working.</summary>
    Running,

    /// <summary>Waiting for the answer to a name clash (<see cref="SfeJobStatus.Conflict"/>).</summary>
    Waiting,

    /// <summary>Finished: each item's result is in <see cref="SfeJobStatus.Results"/>.</summary>
    Completed,

    /// <summary>Stopped before the end: what was done stays.</summary>
    Cancelled,
}
