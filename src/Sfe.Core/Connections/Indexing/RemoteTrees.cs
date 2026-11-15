using Microsoft.EntityFrameworkCore;
using Otools.Net.Sfe.Core.Data;

namespace Otools.Net.Sfe.Core.Connections.Indexing;

/// <summary>
/// The rows of a connection at a key and below it, as a query each provider seeks in its index on
/// <c>(ConnectionId, FullName)</c>: SQL Server's <c>LIKE 'key/%'</c>; on SQLite a range (<c>&gt;= 'key/'</c> and
/// <c>&lt; 'key0'</c>: <c>'0'</c> follows <c>'/'</c>), since its <c>LIKE</c> ignores case and can't use that index.
/// </summary>
internal static class RemoteTrees
{
    public static IQueryable<RemoteFile> InTree(this IQueryable<RemoteFile> rows, SfeDbContext db, int id, string key)
    {
        var (below, end) = (key + "/", key + "0");
        return db.Database.IsSqlServer()
            ? rows.Where(f => f.ConnectionId == id && (f.FullName == key || f.FullName.StartsWith(below)))
            : rows.Where(f =>
                f.ConnectionId == id
                && (
                    f.FullName == key || (string.Compare(f.FullName, below) >= 0 && string.Compare(f.FullName, end) < 0)
                )
            );
    }

    public static IQueryable<RemoteDirectory> InTree(
        this IQueryable<RemoteDirectory> rows,
        SfeDbContext db,
        int id,
        string key
    )
    {
        var (below, end) = (key + "/", key + "0");
        return db.Database.IsSqlServer()
            ? rows.Where(d => d.ConnectionId == id && (d.FullName == key || d.FullName.StartsWith(below)))
            : rows.Where(d =>
                d.ConnectionId == id
                && (
                    d.FullName == key || (string.Compare(d.FullName, below) >= 0 && string.Compare(d.FullName, end) < 0)
                )
            );
    }

    /// <summary>Whether a key is <paramref name="key"/> or below it (the same rule, in memory).</summary>
    public static bool IsInTree(string rowKey, string key) =>
        rowKey == key || rowKey.StartsWith(key + "/", StringComparison.Ordinal);
}
