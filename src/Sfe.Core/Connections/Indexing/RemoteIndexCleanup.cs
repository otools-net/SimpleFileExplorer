using Microsoft.EntityFrameworkCore;
using Otools.Net.Sfe.Core.Data;

namespace Otools.Net.Sfe.Core.Connections.Indexing;

/// <summary>
/// Deletes a connection's rows from a library's index in batches of <see cref="BatchSize"/>, each in its own short
/// statement under the connection's lock (<see cref="ConnectionIndexLocks"/>): what a sync marked removed, what a
/// full listing did not see, or everything (the connection removed, no longer offered to the library, rooted
/// elsewhere). A file's tags go with it (cascade). Folders go after their files, deepest first.
/// </summary>
public sealed class RemoteIndexCleanup(IDbContextFactory<SfeDbContext> dbFactory, ConnectionIndexLocks locks)
{
    public const int BatchSize = 1000;

    /// <summary>Marks removed the rows a full listing (<paramref name="pass"/>) did not see.</summary>
    public async Task MarkUnseenAsync(int connectionId, int pass, CancellationToken cancellationToken)
    {
        using var held = await locks.EnterAsync(connectionId, cancellationToken);
        await using var db = await dbFactory.CreateDbContextAsync(cancellationToken);
        await db
            .RemoteFiles.Where(f => f.ConnectionId == connectionId && f.Pass < pass && !f.Removed)
            .ExecuteUpdateAsync(s => s.SetProperty(f => f.Removed, true), cancellationToken);
        await db
            .RemoteDirectories.Where(d => d.ConnectionId == connectionId && d.Pass < pass && !d.Removed)
            .ExecuteUpdateAsync(s => s.SetProperty(d => d.Removed, true), cancellationToken);
    }

    /// <summary>Deletes the rows marked removed; returns how many files went.</summary>
    public async Task<long> PurgeRemovedAsync(int connectionId, CancellationToken cancellationToken)
    {
        var files = await DeleteFilesAsync(connectionId, removedOnly: true, cancellationToken);
        await DeleteFoldersAsync(connectionId, removedOnly: true, cancellationToken);
        return files;
    }

    /// <summary>Deletes every row of the connection, then its sync state.</summary>
    public async Task ClearAsync(int connectionId, CancellationToken cancellationToken)
    {
        await DeleteFilesAsync(connectionId, removedOnly: false, cancellationToken);
        await DeleteFoldersAsync(connectionId, removedOnly: false, cancellationToken);
        using var held = await locks.EnterAsync(connectionId, cancellationToken);
        await using var db = await dbFactory.CreateDbContextAsync(cancellationToken);
        await db.RemoteIndexes.Where(s => s.ConnectionId == connectionId).ExecuteDeleteAsync(cancellationToken);
    }

    /// <summary>The files and folders in the index (not removed).</summary>
    public async Task<(long Files, long Folders)> CountAsync(int connectionId, CancellationToken cancellationToken)
    {
        await using var db = await dbFactory.CreateDbContextAsync(cancellationToken);
        var files = await db.RemoteFiles.LongCountAsync(
            f => f.ConnectionId == connectionId && !f.Removed,
            cancellationToken
        );
        var folders = await db.RemoteDirectories.LongCountAsync(
            d => d.ConnectionId == connectionId && !d.Removed,
            cancellationToken
        );
        return (files, folders);
    }

    private async Task<long> DeleteFilesAsync(int connectionId, bool removedOnly, CancellationToken cancellationToken)
    {
        long total = 0;
        while (true)
        {
            using var held = await locks.EnterAsync(connectionId, cancellationToken);
            await using var db = await dbFactory.CreateDbContextAsync(cancellationToken);
            var deleted = await db
                .RemoteFiles.Where(f => f.ConnectionId == connectionId && (f.Removed || !removedOnly))
                .OrderBy(f => f.Id)
                .Take(BatchSize)
                .ExecuteDeleteAsync(cancellationToken);
            total += deleted;
            if (deleted < BatchSize)
                return total;
        }
    }

    /// <summary>
    /// Deepest first (a child's key sorts after its parent's), so that no folder goes before the folders in it. A
    /// removed folder still holding what is not removed stays (it would take them with it).
    /// </summary>
    private async Task DeleteFoldersAsync(int connectionId, bool removedOnly, CancellationToken cancellationToken)
    {
        while (true)
        {
            using var held = await locks.EnterAsync(connectionId, cancellationToken);
            await using var db = await dbFactory.CreateDbContextAsync(cancellationToken);
            var rows = db.RemoteDirectories.Where(d => d.ConnectionId == connectionId);
            if (removedOnly)
            {
                rows = rows.Where(d =>
                    d.Removed && !d.Files.Any(f => !f.Removed) && !d.Directories.Any(c => !c.Removed)
                );
            }
            var deleted = await rows.OrderByDescending(d => d.FullName)
                .Take(BatchSize)
                .ExecuteDeleteAsync(cancellationToken);
            if (deleted < BatchSize)
                return;
        }
    }
}
