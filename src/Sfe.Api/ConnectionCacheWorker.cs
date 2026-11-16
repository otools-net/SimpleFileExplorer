using Microsoft.EntityFrameworkCore;
using Otools.Net.Sfe.Api.Maintenance;
using Otools.Net.Sfe.Core.Access;
using Otools.Net.Sfe.Core.Connections.Previews;
using Otools.Net.Sfe.Core.Data;
using Otools.Net.Sfe.Core.Options;

namespace Otools.Net.Sfe.Api;

/// <summary>
/// Keeps the connections' cache (<see cref="ConnectionCacheStore"/>): turns it off at start when its folder is one the
/// app serves (the web root, a library's root); then every <see cref="Interval"/>, once the update of an install is
/// confirmed, removes what nobody used for its age limit and the caches of connections removed meanwhile (with their
/// account, or while the server was off).
/// </summary>
public sealed class ConnectionCacheWorker(
    ConnectionCacheStore store,
    IServiceProvider services,
    MigrationCoordinator migration,
    IWebHostEnvironment environment,
    ILogger<ConnectionCacheWorker> logger
) : BackgroundService
{
    public static readonly TimeSpan Interval = TimeSpan.FromMinutes(5);

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        using var timer = new PeriodicTimer(Interval);
        var checkedServed = false;
        try
        {
            do
            {
                if (migration.IsBlocking)
                    continue;
                try
                {
                    if (!checkedServed)
                        checkedServed = await CheckServedAsync(stoppingToken);
                    store.Sweep(await ConnectionIdsAsync(stoppingToken));
                }
                catch (Exception ex) when (ex is not OperationCanceledException)
                {
                    logger.LogWarning(ex, "Cannot keep the cache of connections");
                }
            } while (await timer.WaitForNextTickAsync(stoppingToken));
        }
        catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested) { }
    }

    /// <summary>Turns the cache off when its folder is one the app serves; true once checked.</summary>
    private async Task<bool> CheckServedAsync(CancellationToken cancellationToken)
    {
        var configuration = services.GetRequiredService<IConfiguration>();
        var tenants = await services.GetRequiredService<AccessService>().GetTenantsAsync(cancellationToken);
        List<string> served =
        [
            environment.WebRootPath ?? string.Empty,
            configuration[$"{SfeOptions.SectionName}:{nameof(SfeOptions.Root)}"] ?? string.Empty,
            .. tenants.Select(tenant => tenant.Root ?? string.Empty),
        ];
        if (store.IsServed(served))
            store.Disable($"its folder {store.Folder} is inside a folder the app serves (a library, the web root)");
        return true;
    }

    private async Task<IReadOnlySet<int>> ConnectionIdsAsync(CancellationToken cancellationToken)
    {
        var factory = services.GetRequiredService<IDbContextFactory<AccessDbContext>>();
        await using var db = await factory.CreateDbContextAsync(cancellationToken);
        return (await db.Connections.Select(c => c.Id).ToListAsync(cancellationToken)).ToHashSet();
    }
}
