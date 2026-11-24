using Microsoft.EntityFrameworkCore;
using Otools.Net.Sfe.Api.Maintenance;
using Otools.Net.Sfe.Core.Connections;
using Otools.Net.Sfe.Core.Data;

namespace Otools.Net.Sfe.Api;

/// <summary>
/// Removes the unfinished uploads into connections (<see cref="RemoteUploads"/>) idle for its limit, and, once the
/// update of an install is confirmed, those into connections removed meanwhile: at start (the records written before
/// a restart are read then) and every <see cref="Interval"/>, so that nobody needs to start or list an upload.
/// </summary>
public sealed class RemoteUploadSweepWorker(
    RemoteUploads uploads,
    IServiceProvider services,
    MigrationCoordinator migration,
    ILogger<RemoteUploadSweepWorker> logger
) : BackgroundService
{
    public static readonly TimeSpan Interval = TimeSpan.FromMinutes(10);

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        using var timer = new PeriodicTimer(Interval);
        try
        {
            do
            {
                try
                {
                    await SweepAsync(stoppingToken);
                }
                catch (Exception ex) when (ex is not OperationCanceledException)
                {
                    logger.LogWarning(ex, "Could not remove the abandoned uploads into connections");
                }
            } while (await timer.WaitForNextTickAsync(stoppingToken));
        }
        catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested) { }
    }

    /// <summary>One pass: idle uploads, then those of connections removed (not while an update waits).</summary>
    public async Task SweepAsync(CancellationToken cancellationToken)
    {
        var removed = uploads.RemoveIdle();
        if (!migration.IsBlocking)
            removed += uploads.RemoveOfConnectionsNotIn(await ConnectionIdsAsync(cancellationToken));
        if (removed > 0)
            logger.LogInformation("Removed {Count} abandoned uploads into connections", removed);
    }

    private async Task<IReadOnlySet<int>> ConnectionIdsAsync(CancellationToken cancellationToken)
    {
        var factory = services.GetRequiredService<IDbContextFactory<AccessDbContext>>();
        await using var db = await factory.CreateDbContextAsync(cancellationToken);
        return (await db.Connections.Select(c => c.Id).ToListAsync(cancellationToken)).ToHashSet();
    }
}
