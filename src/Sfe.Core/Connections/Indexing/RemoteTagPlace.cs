using Microsoft.EntityFrameworkCore;
using Otools.Net.Sfe.Core.Data;

namespace Otools.Net.Sfe.Core.Connections.Indexing;

/// <summary>A connection whose files are tagged, and how its provider compares names (the rows' keys).</summary>
/// <param name="Target">The connection as it is now (root folder, account): rows of an index made of another root or
/// account (dropped at the next sync) are then neither read nor tagged. Null: the rows as they are (a transfer, whose
/// rows SFE's own changes write as they come).</param>
public sealed record RemoteTagPlace(int ConnectionId, bool CaseInsensitive, ConnectionIndexTarget? Target = null)
{
    public static RemoteTagPlace Of(ConnectionIndexTarget target) =>
        new(target.ConnectionId, target.CaseInsensitive, target);

    /// <summary>The key of the row of the file at <paramref name="path"/> (connection path, normalised).</summary>
    public string KeyOf(string path) => RemoteIndexKeys.Key(path, CaseInsensitive);

    /// <summary>The library's rows of the connection are of its current root and account (or none is asked).</summary>
    public async Task<bool> IsCurrentAsync(SfeDbContext db, CancellationToken cancellationToken)
    {
        if (Target is not { } target)
            return true;
        var state = await db
            .RemoteIndexes.AsNoTracking()
            .FirstOrDefaultAsync(s => s.ConnectionId == ConnectionId, cancellationToken);
        return state is not null && state.RootPath == target.RootPath && state.AccountKey == target.AccountKey;
    }
}
