using Microsoft.EntityFrameworkCore;
using Otools.Net.Sfe.Core.Data;

namespace Otools.Net.Sfe.Core.Connections.Indexing;

/// <summary>
/// A folder SFE renamed or moved inside a connection: its rows and those below it take the new path in a few
/// statements (no row read), so the index shows the move at once and the files keep their tags. Rows that would
/// pass the index's path length are marked removed instead (the next sync drops them); rows left at the new path
/// by an index out of date are set aside first.
/// </summary>
internal static class RemoteTreeMoves
{
    public static async Task MoveAsync(
        SfeDbContext db,
        RemoteIndexScope scope,
        (string From, RemoteItem Item) move,
        CancellationToken cancellationToken
    )
    {
        var (fromKey, toKey) = (Key(scope, move.From), Key(scope, move.Item.Path));
        if (fromKey != toKey)
            await SetAsideAsync(db, scope.ConnectionId, toKey, cancellationToken);
        await RewriteAsync(db, scope.ConnectionId, (fromKey, move.From), (toKey, move.Item.Path), cancellationToken);
    }

    private static async Task SetAsideAsync(SfeDbContext db, int id, string key, CancellationToken cancellationToken)
    {
        await db
            .RemoteFiles.InTree(db, id, key)
            .ExecuteUpdateAsync(
                s =>
                    s.SetProperty(f => f.FullName, f => RemoteIndexKeys.ParkedPrefix + f.Id.ToString())
                        .SetProperty(f => f.Removed, true),
                cancellationToken
            );
        await db
            .RemoteDirectories.InTree(db, id, key)
            .ExecuteUpdateAsync(
                s =>
                    s.SetProperty(d => d.FullName, d => RemoteIndexKeys.ParkedPrefix + d.Id.ToString())
                        .SetProperty(d => d.Removed, true),
                cancellationToken
            );
    }

    private static async Task RewriteAsync(
        SfeDbContext db,
        int id,
        (string Key, string Path) from,
        (string Key, string Path) to,
        CancellationToken cancellationToken
    )
    {
        var (cut, keyStart, pathStart) = (from.Key.Length, to.Key, to.Path);
        var longest = SfeDbContext.MaxPathLength - to.Key.Length + cut;
        await db
            .RemoteFiles.InTree(db, id, from.Key)
            .Where(f => f.FullName.Length > longest)
            .ExecuteUpdateAsync(s => s.SetProperty(f => f.Removed, true), cancellationToken);
        await db
            .RemoteDirectories.InTree(db, id, from.Key)
            .Where(d => d.FullName.Length > longest)
            .ExecuteUpdateAsync(s => s.SetProperty(d => d.Removed, true), cancellationToken);
        await db
            .RemoteFiles.InTree(db, id, from.Key)
            .Where(f => f.FullName.Length <= longest)
            .ExecuteUpdateAsync(
                s =>
                    s.SetProperty(f => f.FullName, f => keyStart + f.FullName.Substring(cut))
                        .SetProperty(f => f.DisplayPath, f => pathStart + f.DisplayPath.Substring(cut)),
                cancellationToken
            );
        await db
            .RemoteDirectories.InTree(db, id, from.Key)
            .Where(d => d.FullName.Length <= longest)
            .ExecuteUpdateAsync(
                s =>
                    s.SetProperty(d => d.FullName, d => keyStart + d.FullName.Substring(cut))
                        .SetProperty(d => d.DisplayPath, d => pathStart + d.DisplayPath.Substring(cut)),
                cancellationToken
            );
    }

    private static string Key(RemoteIndexScope scope, string path) => RemoteIndexKeys.Key(path, scope.CaseInsensitive);
}
