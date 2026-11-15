using Microsoft.EntityFrameworkCore;
using Otools.Net.Sfe.Core.Data;

namespace Otools.Net.Sfe.Core.Connections.Indexing;

/// <summary>
/// One batch of writes on a connection's rows: under its lock (<see cref="ConnectionIndexLocks"/>), in one
/// transaction run by the provider's execution strategy (SQL Server retries the whole batch, on a fresh context).
/// </summary>
public sealed class RemoteIndexTransactions(IDbContextFactory<SfeDbContext> dbFactory, ConnectionIndexLocks locks)
{
    public async Task WriteAsync(
        int connectionId,
        Func<SfeDbContext, CancellationToken, Task> work,
        CancellationToken cancellationToken
    )
    {
        using var held = await locks.EnterAsync(connectionId, cancellationToken);
        await using var outer = await dbFactory.CreateDbContextAsync(cancellationToken);
        await outer
            .Database.CreateExecutionStrategy()
            .ExecuteAsync(
                async token =>
                {
                    await using var db = await dbFactory.CreateDbContextAsync(token);
                    await using var transaction = await db.Database.BeginTransactionAsync(token);
                    await work(db, token);
                    await db.SaveChangesAsync(token);
                    await transaction.CommitAsync(token);
                },
                cancellationToken
            );
    }

    /// <summary>A context to read with (writes go through <see cref="WriteAsync"/>).</summary>
    public Task<SfeDbContext> ContextAsync(CancellationToken cancellationToken) =>
        dbFactory.CreateDbContextAsync(cancellationToken);

    /// <summary>The connection's sync state, or null when it isn't indexed in this library.</summary>
    public async Task<RemoteIndexState?> StateAsync(int connectionId, CancellationToken cancellationToken)
    {
        await using var db = await dbFactory.CreateDbContextAsync(cancellationToken);
        return await db
            .RemoteIndexes.AsNoTracking()
            .FirstOrDefaultAsync(s => s.ConnectionId == connectionId, cancellationToken);
    }
}
