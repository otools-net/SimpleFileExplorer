using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Sfe.Core.Options;

namespace Sfe.Core.Data;

public static class SfeDatabase
{
    /// <remarks>
    /// The search counts its matches up to a cap with an unordered <c>Take(n).Count()</c>: any n rows give the same
    /// count, and an ordering would cost a sort of every match (even <c>ORDER BY (SELECT 1)</c> on SQLite). EF can't
    /// silence a warning for one query, so the row limiting one is off; pages must still be ordered.
    /// </remarks>
    /// <param name="migrationsHistoryTable">
    /// The access tables (<see cref="AccessDbContext"/>) share the first library's database and keep their own history.
    /// </param>
    public static DbContextOptionsBuilder Configure(DbContextOptionsBuilder builder, DatabaseProvider provider, string connectionString,
        string migrationsHistoryTable = "__EFMigrationsHistory") =>
        (provider switch
        {
            DatabaseProvider.SqlServer => builder.UseSqlServer(connectionString, sql =>
            {
                sql.MigrationsHistoryTable(migrationsHistoryTable, SfeDbContext.Schema);
                sql.EnableRetryOnFailure();
            }),
            DatabaseProvider.Sqlite => builder.UseSqlite(connectionString),
            _ => throw new NotSupportedException($"Database provider '{provider}' is not supported."),
        }).ConfigureWarnings(w => w.Ignore(CoreEventId.RowLimitingOperationWithoutOrderByWarning));

    /// <summary>
    /// Makes a relative SQLite <c>Data Source</c> relative to <paramref name="basePath"/> instead of the current
    /// directory, and creates its folder (SQLite creates the file, not the folders above it).
    /// </summary>
    public static string PrepareSqlite(string connectionString, string? basePath)
    {
        var builder = new SqliteConnectionStringBuilder(connectionString);
        var dataSource = builder.DataSource;
        if (string.IsNullOrWhiteSpace(dataSource) || dataSource == ":memory:" || builder.Mode == SqliteOpenMode.Memory
            || dataSource.StartsWith("file:", StringComparison.OrdinalIgnoreCase))
        {
            return connectionString;
        }

        var fullPath = Path.GetFullPath(dataSource, basePath ?? Directory.GetCurrentDirectory());
        if (Path.GetDirectoryName(fullPath) is { Length: > 0 } directory)
            Directory.CreateDirectory(directory);

        builder.DataSource = fullPath;
        return builder.ToString();
    }
}
