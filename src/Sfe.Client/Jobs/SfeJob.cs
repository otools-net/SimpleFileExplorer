using System;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using Otools.Net.Sfe.Client.Http;
using Otools.Net.Sfe.Client.Jobs;

namespace Otools.Net.Sfe.Client;

/// <summary>
/// A move or copy running on the server (in the library, or a transfer with a connection): followed until it ends,
/// its name clashes answered, or stopped. The server keeps a job in memory (the last 50 finished): a restart of the
/// server forgets it.
/// </summary>
public sealed class SfeJob
{
    /// <summary>How long an answered job is given to leave its waiting.</summary>
    private static readonly TimeSpan AnswerSettling = TimeSpan.FromSeconds(1);

    /// <summary>How often it is asked meanwhile.</summary>
    private static readonly TimeSpan AnswerPoll = TimeSpan.FromMilliseconds(20);

    private readonly SfeTransport _transport;

    internal SfeJob(SfeTransport transport, SfeJobStatus status)
    {
        _transport = transport;
        Status = status;
    }

    /// <summary>The job's id on the server.</summary>
    public int Id => Status.Id;

    /// <summary>Where the job stood when last asked (at its start, or by the last call of this object).</summary>
    public SfeJobStatus Status { get; private set; }

    /// <summary>Asks the server where the job stands now.</summary>
    /// <param name="cancellationToken">Stops waiting for the server.</param>
    public async Task<SfeJobStatus> RefreshAsync(CancellationToken cancellationToken = default)
    {
        Status = await _transport
            .ReadAsync<SfeJobStatus>(SfeRequest.Get(Route()), cancellationToken)
            .ConfigureAwait(false);
        return Status;
    }

    /// <summary>
    /// Follows the job until it ends, answering its name clashes with <see cref="SfeJobWaitOptions.OnConflict"/>, and
    /// answers its last state (each item's result in <see cref="SfeJobStatus.Results"/>). Cancelling stops the
    /// waiting, not the job: <see cref="CancelAsync"/> does.
    /// </summary>
    /// <param name="options">Progress and the answer to clashes; none: a clash fails the wait.</param>
    /// <param name="cancellationToken">Stops waiting.</param>
    /// <exception cref="SfeJobConflictException">The job waits on a clash and nothing answers it.</exception>
    public async Task<SfeJobStatus> WaitAsync(
        SfeJobWaitOptions? options = null,
        CancellationToken cancellationToken = default
    )
    {
        var status = Status;
        while (true)
        {
            options?.Progress?.Report(status);
            if (status.IsFinished)
                return status;
            if (status is { State: SfeJobState.Waiting, Conflict: { } conflict })
            {
                // Answering asks where the job stands afterwards.
                await AnswerConflictAsync(conflict, options, cancellationToken).ConfigureAwait(false);
                status = Status;
                continue;
            }
            await Task.Delay(_transport.Options.PollInterval, cancellationToken).ConfigureAwait(false);
            status = await RefreshAsync(cancellationToken).ConfigureAwait(false);
        }
    }

    /// <summary>
    /// Answers the name clash the job waits on, then asks where the job stands (<see cref="Status"/>). Nothing happens
    /// when it no longer waits (answered meanwhile).
    /// </summary>
    /// <param name="answer">What to do, and whether for the next clashes too.</param>
    /// <param name="cancellationToken">Stops waiting for the server.</param>
    public async Task AnswerAsync(SfeConflictAnswer answer, CancellationToken cancellationToken = default)
    {
        // A 409 says the job asks nothing now: it was answered, or it ended.
        var body = new JobAnswer(answer.Choice, answer.ForAll);
        var request = new SfeRequest(HttpMethod.Post, Route("/answer"))
        {
            Content = () => SfeJson.Content(body),
            AcceptedStatus = 409,
        };
        using var response = await _transport.SendAsync(request, cancellationToken).ConfigureAwait(false);
        if (response.StatusCode == System.Net.HttpStatusCode.Conflict)
        {
            await RefreshAsync(cancellationToken).ConfigureAwait(false);
            return;
        }
        await WaitPastAnswerAsync(cancellationToken).ConfigureAwait(false);
    }

    /// <summary>
    /// Stops the job (what is done stays, nothing half copied does), then asks where it stands
    /// (<see cref="Status"/>): a job waiting on a clash leaves its waiting a moment later, so a
    /// <see cref="WaitAsync"/> right after sees it end rather than the old clash.
    /// </summary>
    /// <param name="cancellationToken">Stops waiting for the server.</param>
    public async Task CancelAsync(CancellationToken cancellationToken = default)
    {
        await _transport.RunAsync(SfeRequest.Post(Route("/cancel")), cancellationToken).ConfigureAwait(false);
        await WaitPastAnswerAsync(cancellationToken).ConfigureAwait(false);
    }

    /// <summary>
    /// Starts again, as a new job, the items of an ended transfer with a connection that failed or were not reached
    /// (the same places and clash choice; a file already there of the same size counts as done), and answers that
    /// job. The server keeps what to retry 24 hours after the job ended, while it runs.
    /// </summary>
    /// <param name="cancellationToken">Stops waiting for the server (not the job).</param>
    /// <exception cref="SfeConflictException"><c>job-running</c>: it hasn't ended; <c>nothing-to-retry</c>: nothing
    /// failed, or it was started again already, or is too old.</exception>
    /// <exception cref="SfeNotFoundException">Not a transfer of the caller's (moves and copies inside one place are
    /// not retried).</exception>
    public async Task<SfeJob> RetryAsync(CancellationToken cancellationToken = default)
    {
        var status = await _transport
            .ReadAsync<SfeJobStatus>(SfeRequest.Post(Route("/retry")), cancellationToken)
            .ConfigureAwait(false);
        return new SfeJob(_transport, status);
    }

    /// <summary>
    /// Asks where the job stands until it no longer waits: the server takes an answer (or a cancel) at once but the
    /// job leaves its waiting a moment later. Bounded, as a next clash may come that soon; <see cref="WaitAsync"/>
    /// then asks it.
    /// </summary>
    private async Task WaitPastAnswerAsync(CancellationToken cancellationToken)
    {
        var deadline = DateTime.UtcNow + AnswerSettling;
        while (
            (await RefreshAsync(cancellationToken).ConfigureAwait(false)).State == SfeJobState.Waiting
            && DateTime.UtcNow < deadline
        )
        {
            await Task.Delay(AnswerPoll, cancellationToken).ConfigureAwait(false);
        }
    }

    private async Task AnswerConflictAsync(
        SfeJobConflict conflict,
        SfeJobWaitOptions? options,
        CancellationToken cancellationToken
    )
    {
        if (options?.OnConflict is not { } answerer)
            throw new SfeJobConflictException(this, conflict);
        var answer = await answerer(conflict, cancellationToken).ConfigureAwait(false);
        await AnswerAsync(answer, cancellationToken).ConfigureAwait(false);
    }

    private string Route(string action = "") =>
        Routes.V1($"files/jobs/{Id.ToString(System.Globalization.CultureInfo.InvariantCulture)}{action}");
}
