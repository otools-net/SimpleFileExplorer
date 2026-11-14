using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Sfe.Core.Files;
using Sfe.Core.Options;
using Sfe.Core.Tagging;
using Sfe.Core.Tasks;

namespace Sfe.Core.Transfers;

/// <summary>
/// Moves and copies between the library and a connection, or between two connections
/// (agents/docs/features/transfers.md): background jobs of the library's <see cref="JobBoard"/>
/// (the same ids, list, questions on name clashes and cancel as <see cref="FileJobs"/>' jobs), walked by
/// <see cref="TransferRun"/>. Each job owns its two sides and disposes them when it ends, is one run of the task
/// history, and leaves its failed items to be started again (<see cref="Retry"/>).
/// </summary>
public sealed class TransferJobs(
    JobBoard board,
    TagService tags,
    TaskHistory history,
    IOptionsMonitor<SfeOptions> options,
    ILogger<TransferJobs> logger
) : IDisposable
{
    /// <summary>Items asked in one job.</summary>
    public const int MaxItems = 1000;

    private readonly CancellationTokenSource _shutdown = new();
    private readonly TransferRetries _retries = new();

    /// <summary>Starts the transfer in the background; the result is its first view.</summary>
    public FileJobView Start(TransferStart start)
    {
        start = start with { MaxFileMegabytes = start.To.IsLibrary ? null : MaxFileMegabytes() };
        var request = new FileJobRequest(start.Kind, start.Paths, start.To.Path, start.Conflicts);
        var job = new JobRun(
            board.NextId(),
            request,
            start.Actor,
            start.Access,
            CancellationTokenSource.CreateLinkedTokenSource(_shutdown.Token)
        )
        {
            Partials = start.Partials,
            FromConnection = start.FromConnection,
            ToConnection = start.To.ConnectionId,
        };
        board.Add(job);
        job.Completion = Task.Run(() => RunAsync(job, start), CancellationToken.None);
        board.Prune();
        return job.View();
    }

    /// <summary>What an ended job of <paramref name="ownerId"/> left to start again; null when nothing.</summary>
    public TransferRetry? FindRetry(int jobId, int? ownerId) =>
        _retries.Find(jobId) is { } retry && ownerId is not null && retry.OwnerId == ownerId ? retry : null;

    /// <summary>A transfer job of <paramref name="ownerId"/> the board still shows; null for any other job.</summary>
    public FileJobView? Find(int jobId, int? ownerId) =>
        board.Find(jobId) is { } view
        && ownerId is not null
        && view.OwnerId == ownerId
        && (view.FromConnection ?? view.ToConnection) is not null
            ? view
            : null;

    /// <summary>
    /// Starts the failed items of the ended job <paramref name="jobId"/> again, as a new job (its view); null when
    /// they were started again already. <paramref name="start"/> names those items, the same places and choice.
    /// </summary>
    public FileJobView? Retry(int jobId, TransferStart start) =>
        _retries.Claim(jobId) ? Start(start with { Retry = true }) : null;

    public void Dispose()
    {
        _shutdown.Cancel();
        _shutdown.Dispose();
    }

    private async Task RunAsync(JobRun job, TransferStart start)
    {
        var run = history.Begin(BackgroundTaskKind.Transfer, null, TaskTrigger.Manual);
        var outcome = TaskOutcome.Completed;
        await using var source = start.Source;
        await using var target = start.Target;
        try
        {
            await ItemsAsync(job, new TransferRun(job, start, logger), job.Cancellation.Token);
            End(job, start, FileJobState.Completed);
        }
        catch (OperationCanceledException)
        {
            outcome = TaskOutcome.Cancelled;
            End(job, start, FileJobState.Cancelled);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "The transfer job {Id} failed", job.Id);
            outcome = TaskOutcome.Failed;
            End(job, start, FileJobState.Completed);
        }
        finally
        {
            // Tagged files may have left the library (a move out goes to the trash).
            if (start.FromConnection is null)
                tags.InvalidateFileCounts();
            await run.CompleteAsync(outcome, TransferSummary.Of(start, job.View(), job.State));
        }
    }

    /// <summary>The failed items kept to start again, then the job ended (a retry asked at once finds them).</summary>
    private void End(JobRun job, TransferStart start, FileJobState state)
    {
        _retries.Keep(TransferRetry.Of(job.Id, start, job.View()));
        job.Finish(state);
    }

    private async Task ItemsAsync(JobRun job, TransferRun run, CancellationToken token)
    {
        var folder = await run.TargetFolderAsync(token);
        foreach (var path in job.Request.Paths.Distinct(StringComparer.Ordinal))
        {
            token.ThrowIfCancellationRequested();
            job.Current = path;
            var result = folder is null ? run.NoTarget(path) : await run.ItemAsync(path, folder, token);
            job.Report(result);
            if (result.Status != FileOperationStatus.Done)
                logger.LogWarning("{Path}: {Status}. {Problem}", path, result.Status, result.Message);
        }
    }

    private int? MaxFileMegabytes() =>
        options.CurrentValue.Files.Uploads.MaxFileSizeMegabytes is > 0 and var max ? max : null;
}
