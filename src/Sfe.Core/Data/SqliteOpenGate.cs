using System.Data.Common;
using Microsoft.EntityFrameworkCore.Diagnostics;

namespace Sfe.Core.Data;

/// <summary>
/// Opens SQLite connections one at a time, process-wide.
/// </summary>
/// <remarks>
/// Works around a race in Microsoft.Data.Sqlite's pool (10.0): <c>SqliteConnectionFactory.GetConnection</c> takes a
/// pooled native connection under the pool's lock but marks it as in use (<c>Activate</c>: <c>_active</c>, then the
/// owner) after releasing it, while an <c>Open</c> finding the pool empty reclaims as "leaked" every connection that
/// is active without an owner. Between the two writes, a connection being handed out goes back into the pool and a
/// third <c>Open</c> gets the same native connection: two DbContexts then share it, which showed as "database is
/// locked" from <c>Open</c> (EF's <c>sqlite3_create_function</c> refused while the other thread ran a statement) when
/// a scan's parallel folder workers opened their first connections, and could as well mix their statements silently.
/// Both sides only run inside <c>SqliteConnection.Open</c>, so serialising it closes the window; a pooled open costs
/// microseconds (the native file is opened only when the pool grows).
/// </remarks>
internal sealed class SqliteOpenGate : DbConnectionInterceptor
{
    public static readonly SqliteOpenGate Instance = new();

    private static readonly Lock Gate = new();

    public override InterceptionResult ConnectionOpening(DbConnection connection, ConnectionEventData eventData, InterceptionResult result)
    {
        if (result.IsSuppressed)
            return result;
        Open(connection);
        return InterceptionResult.Suppress();
    }

    public override ValueTask<InterceptionResult> ConnectionOpeningAsync(DbConnection connection, ConnectionEventData eventData,
        InterceptionResult result, CancellationToken cancellationToken = default)
    {
        if (result.IsSuppressed)
            return ValueTask.FromResult(result);
        cancellationToken.ThrowIfCancellationRequested();
        // SqliteConnection.OpenAsync is synchronous anyway.
        Open(connection);
        return ValueTask.FromResult(InterceptionResult.Suppress());
    }

    private static void Open(DbConnection connection)
    {
        lock (Gate)
            connection.Open();
    }
}
