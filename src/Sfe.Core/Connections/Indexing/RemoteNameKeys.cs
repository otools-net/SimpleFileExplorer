using Microsoft.EntityFrameworkCore;
using Otools.Net.Sfe.Core.Library;

namespace Otools.Net.Sfe.Core.Connections.Indexing;

/// <summary>
/// Fills the name keys (<see cref="Data.RemoteFile.NameKey"/>) of a connection's rows indexed before the column
/// existed: batches of <see cref="BatchSize"/>, each under the connection's lock, at the start of each sync. Until a
/// folder's rows all have one, listings of it come from the provider (<see cref="RemoteIndexListings"/>).
/// </summary>
internal static class RemoteNameKeys
{
    public const int BatchSize = 1000;

    public static async Task FillAsync(RemoteIndexTransactions store, int connectionId, CancellationToken token)
    {
        while (await FillBatchAsync(store, connectionId, token) > 0) { }
    }

    /// <returns>The rows filled.</returns>
    private static async Task<int> FillBatchAsync(RemoteIndexTransactions store, int id, CancellationToken token)
    {
        var filled = 0;
        await store.WriteAsync(
            id,
            async (db, inner) =>
            {
                var files = await db
                    .RemoteFiles.Where(f => f.ConnectionId == id && f.NameKey == null)
                    .OrderBy(f => f.Id)
                    .Take(BatchSize)
                    .ToListAsync(inner);
                foreach (var file in files)
                    file.NameKey = NaturalComparer.KeyOf(file.Name + file.Extension);
                var folders = await db
                    .RemoteDirectories.Where(d => d.ConnectionId == id && d.NameKey == null)
                    .OrderBy(d => d.Id)
                    .Take(BatchSize)
                    .ToListAsync(inner);
                foreach (var folder in folders)
                    folder.NameKey = NaturalComparer.KeyOf(folder.Name);
                filled = files.Count + folders.Count;
            },
            token
        );
        return filled;
    }
}
