using System.Security.Cryptography;
using System.Text;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Otools.Net.Sfe.Core.Tasks;

namespace Otools.Net.Sfe.Core.Connections.Indexing;

/// <summary>
/// One sync of one connection in a library, as a run of the "Connection index" task (history and log): the
/// connection read from the accounts' database, opened with its credentials, synced through its gate
/// (<see cref="GatedIndexSource"/>); a refused sign-in marks it to be signed in to again. An account's own
/// connection is not named in the run (the library's administrators read the history).
/// </summary>
internal sealed class ConnectionIndexJob(
    ConnectionIndexer indexer,
    RemoteIndexTransactions store,
    TaskHistory history,
    ConnectionIndexSources sources,
    ILogger logger
)
{
    public async Task RunAsync(ConnectionInfo info, ConnectionIndexRun sync, (TaskTrigger, int?) started)
    {
        var (trigger, scheduleId) = started;
        var run = history.Begin(BackgroundTaskKind.ConnectionIndex, sync.Name, trigger, scheduleId);
        var (outcome, summary) = await SyncAsync(info, sync, sync.Stop.Token);
        await run.CompleteAsync(outcome, summary);
    }

    /// <summary>A hash of the provider's account, so that another account is told apart without keeping it.</summary>
    public static string? AccountKeyOf(string? account) =>
        account is null ? null : Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(account)));

    private async Task<(TaskOutcome, string)> SyncAsync(
        ConnectionInfo info,
        ConnectionIndexRun sync,
        CancellationToken cancellationToken
    )
    {
        var id = info.Id;
        if (info.Disabled)
            return (TaskOutcome.Skipped, "The connection is off.");
        if (sources.Providers.Find(info.Kind) is not { } provider)
            return (TaskOutcome.Skipped, "Its kind of storage is not on this server.");
        var opened = await sources.Connections.OpenAsync(id, cancellationToken);
        if (opened.Value is not { } storage)
            return await FailedAsync(id, opened.Problem ?? ConnectionService.UnusableProblem);

        await using (storage)
        {
            var target = new ConnectionIndexTarget(
                id,
                info.RootPath,
                AccountKeyOf(info.Account),
                provider.Capabilities,
                Private: info.Level == ConnectionLevel.Account
            );
            var source = new GatedIndexSource(storage, sources.Calls, id);
            return await TrySyncAsync(target, source, sync, cancellationToken);
        }
    }

    private async Task<(TaskOutcome, string)> TrySyncAsync(
        ConnectionIndexTarget target,
        IRemoteIndexSource source,
        ConnectionIndexRun sync,
        CancellationToken cancellationToken
    )
    {
        var id = target.ConnectionId;
        try
        {
            var result = await indexer.SyncAsync(target, source, sync.Progress, cancellationToken);
            logger.LogInformation("Connection {ConnectionId} synced: {Summary}", id, result.Summary());
            return (TaskOutcome.Completed, result.Summary());
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            return (TaskOutcome.Cancelled, "Stopped: the next sync goes on from where it stopped.");
        }
        catch (RemoteStorageException error)
        {
            if (error.Kind == RemoteErrorKind.CredentialsEnded)
                await sources.Connections.MarkNeedsReconnectingAsync(id, CancellationToken.None);
            logger.LogWarning(
                "Connection {ConnectionId}: the sync stopped: {Kind} {Error}",
                id,
                error.Kind,
                error.Message
            );
            return await FailedAsync(id, $"The provider: {error.Message}");
        }
        catch (Exception error) when (error is not OperationCanceledException)
        {
            logger.LogError(error, "Connection {ConnectionId}: the sync stopped", id);
            return await FailedAsync(id, error.Message);
        }
    }

    /// <summary>The sync failed: told in the run and in the index's status.</summary>
    private async Task<(TaskOutcome, string)> FailedAsync(int id, string problem)
    {
        var text = problem.Length > 1000 ? problem[..1000] : problem;
        try
        {
            await using var db = await store.ContextAsync(CancellationToken.None);
            await db
                .RemoteIndexes.Where(s => s.ConnectionId == id)
                .ExecuteUpdateAsync(s => s.SetProperty(r => r.LastError, text));
        }
        catch (Exception error) when (error is not OperationCanceledException)
        {
            logger.LogWarning(error, "Connection {ConnectionId}: its sync's problem could not be written", id);
        }
        return (TaskOutcome.Failed, text);
    }
}
