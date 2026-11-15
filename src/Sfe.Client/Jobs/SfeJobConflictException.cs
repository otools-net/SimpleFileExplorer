namespace Otools.Net.Sfe.Client;

/// <summary>
/// A job waits on a name clash and nobody was there to answer it (<see cref="SfeJobWaitOptions.OnConflict"/>): the job
/// keeps waiting for <see cref="SfeJob.AnswerAsync"/> or <see cref="SfeJob.CancelAsync"/>.
/// </summary>
public sealed class SfeJobConflictException : SfeException
{
    /// <summary>Job <paramref name="job"/> waits on <paramref name="conflict"/>.</summary>
    /// <param name="job">The job.</param>
    /// <param name="conflict">The clash it waits on.</param>
    public SfeJobConflictException(SfeJob job, SfeJobConflict conflict)
        : base($"Job {job.Id} waits for an answer: \"{conflict.Source.Name}\" is already in its target.")
    {
        Job = job;
        Conflict = conflict;
    }

    /// <summary>The job, still waiting.</summary>
    public SfeJob Job { get; }

    /// <summary>The clash it waits on.</summary>
    public SfeJobConflict Conflict { get; }
}
