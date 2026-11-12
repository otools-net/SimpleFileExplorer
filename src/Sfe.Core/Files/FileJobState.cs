namespace Sfe.Core.Files;

public enum FileJobState
{
    Running,

    /// <summary>Waits for the person's answer to a name clash (<see cref="FileJobView.Conflict"/>).</summary>
    Waiting,

    Completed,
    Cancelled,
}
