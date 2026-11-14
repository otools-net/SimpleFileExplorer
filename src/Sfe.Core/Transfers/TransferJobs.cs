using Microsoft.Extensions.Logging;
using Sfe.Core.Files;
using Sfe.Core.Tagging;

namespace Sfe.Core.Transfers;

/// <summary>
/// Moves and copies between the library and a connection, or between two connections
/// (agents/docs/development/connections-plan.md, phase 5): background jobs of the library's <see cref="JobBoard"/>
/// (the same ids, list, questions on name clashes and cancel as <see cref="FileJobs"/>' jobs), walked by
/// <see cref="TransferRun"/>. Each job owns its two sides and disposes them when it ends.
/// </summary>
public sealed class TransferJobs(JobBoard board, TagService tags, ILogger<TransferJobs> logger) : IDisposable
{
    /// <summary>Items asked in one job.</summary>
    public const int MaxItems = 1000;

    private readonly CancellationTokenSource _shutdown = new();

    /// <summary>Starts the transfer in the background; the result is its first view.</summary>
    public FileJobView Start(TransferStart start)
    {
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

    public void Dispose()
    {
        _shutdown.Cancel();
        _shutdown.Dispose();
    }

    private async Task RunAsync(JobRun job, TransferStart start)
    {
        await using var source = start.Source;
        await using var target = start.Target;
        try
        {
            await ItemsAsync(job, new TransferRun(job, start, logger), job.Cancellation.Token);
            job.Finish(FileJobState.Completed);
        }
        catch (OperationCanceledException)
        {
            job.Finish(FileJobState.Cancelled);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "The transfer job {Id} failed", job.Id);
            job.Finish(FileJobState.Completed);
        }
        finally
        {
            // Tagged files may have left the library (a move out goes to the trash).
            if (start.FromConnection is null)
                tags.InvalidateFileCounts();
        }
    }

    private static async Task ItemsAsync(JobRun job, TransferRun run, CancellationToken token)
    {
        var folder = await run.TargetFolderAsync(token);
        foreach (var path in job.Request.Paths.Distinct(StringComparer.Ordinal))
        {
            token.ThrowIfCancellationRequested();
            job.Current = path;
            job.Report(folder is null ? run.NoTarget(path) : await run.ItemAsync(path, folder, token));
        }
    }
}
