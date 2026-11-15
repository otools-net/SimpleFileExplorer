using System;
using System.Threading;
using System.Threading.Tasks;

namespace Otools.Net.Sfe.Client;

/// <summary>How <see cref="SfeJob.WaitAsync"/> follows a job.</summary>
public sealed class SfeJobWaitOptions
{
    /// <summary>Told each time the job is asked where it stands.</summary>
    public IProgress<SfeJobStatus>? Progress { get; set; }

    /// <summary>
    /// Answers the name clashes the job waits on. Without it, a clash fails the wait with an
    /// <see cref="SfeJobConflictException"/> and the job keeps waiting (<see cref="SfeJob.AnswerAsync"/>).
    /// </summary>
    public Func<SfeJobConflict, CancellationToken, Task<SfeConflictAnswer>>? OnConflict { get; set; }
}
