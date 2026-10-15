using Sfe.Api.Maintenance;
using Sfe.Core.Logs;

namespace Sfe.Api;

/// <summary>
/// Writes the server's log every <see cref="FlushInterval"/> (and once more when the server stops), and forgets its
/// old entries every <see cref="PurgeInterval"/>. Until the update of an install is confirmed nothing goes to the
/// database: the entries wait in memory, the daily files still get them.
/// </summary>
public sealed class AuditLogWorker(AuditLogService log, MigrationCoordinator migration, ILogger<AuditLogWorker> logger) : BackgroundService
{
    public static readonly TimeSpan FlushInterval = TimeSpan.FromSeconds(2);
    public static readonly TimeSpan PurgeInterval = TimeSpan.FromHours(1);

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        var nextPurge = DateTime.UtcNow;
        using var timer = new PeriodicTimer(FlushInterval);
        try
        {
            do
            {
                try
                {
                    var database = !migration.IsBlocking;
                    await log.FlushAsync(database, stoppingToken);
                    if (database && DateTime.UtcNow >= nextPurge)
                    {
                        nextPurge = DateTime.UtcNow + PurgeInterval;
                        await log.PurgeAsync(stoppingToken);
                    }
                }
                catch (Exception ex) when (ex is not OperationCanceledException)
                {
                    logger.LogError(ex, "Cannot write the server's log");
                }
            }
            while (await timer.WaitForNextTickAsync(stoppingToken));
        }
        catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
        {
        }
    }

    public override async Task StopAsync(CancellationToken cancellationToken)
    {
        await base.StopAsync(cancellationToken);
        try
        {
            await log.FlushAsync(!migration.IsBlocking, cancellationToken);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            logger.LogWarning(ex, "Cannot write the last entries of the server's log");
        }
    }
}
