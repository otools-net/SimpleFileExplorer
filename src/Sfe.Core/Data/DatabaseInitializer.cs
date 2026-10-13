using System.Text.RegularExpressions;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata;
using Microsoft.Extensions.Logging;

namespace Sfe.Core.Data;

/// <summary>Creates or updates a library's index database.</summary>
public sealed class DatabaseInitializer(IDbContextFactory<SfeDbContext> dbFactory, ILogger<DatabaseInitializer> logger)
{
    public async Task InitializeAsync(CancellationToken cancellationToken = default)
    {
        await using var db = await dbFactory.CreateDbContextAsync(cancellationToken);
        await DatabaseSchema.InitializeAsync(db, logger, cancellationToken);
    }
}

/// <summary>Creates or updates the database of the accounts, groups and libraries (<see cref="AccessDbContext"/>).</summary>
public sealed class AccessDatabaseInitializer(IDbContextFactory<AccessDbContext> dbFactory, ILogger<AccessDatabaseInitializer> logger)
{
    public async Task InitializeAsync(CancellationToken cancellationToken = default)
    {
        await using var db = await dbFactory.CreateDbContextAsync(cancellationToken);
        await DatabaseSchema.InitializeAsync(db, logger, cancellationToken);
    }
}

/// <summary>
/// Migrations on SQL Server; on other providers the schema comes straight from the model. Two contexts can share a
/// database (the access tables live next to the first library's index): each has its own migrations history, and on
/// SQLite each adds the tables it misses.
/// </summary>
internal static partial class DatabaseSchema
{
    public static async Task InitializeAsync(DbContext db, ILogger logger, CancellationToken cancellationToken)
    {
        if (!db.Database.IsSqlServer())
        {
            if (db.Database.IsSqlite())
            {
                // Write-ahead logging: searches keep reading while a scan writes (the default rollback journal
                // blocks readers during each commit). The mode is stored in the database file.
                await db.Database.ExecuteSqlRawAsync("PRAGMA journal_mode = WAL;", cancellationToken);
            }

            if (!await db.Database.EnsureCreatedAsync(cancellationToken) && db.Database.IsSqlite())
                await AddMissingSqliteObjectsAsync(db, logger, cancellationToken);
            return;
        }

        await db.Database.MigrateAsync(cancellationToken);
    }

    /// <summary>
    /// EnsureCreated does nothing when the database exists, so a SQLite file created by an earlier version (or by the
    /// other context) lacks the tables and indexes added since. Create them, with the seed rows of new tables, from
    /// the model's script, and add the new nullable columns of existing tables (other column changes are not handled).
    /// </summary>
    private static async Task AddMissingSqliteObjectsAsync(DbContext db, ILogger logger, CancellationToken cancellationToken)
    {
        var tables = await SqliteObjectsAsync(db, "table", cancellationToken);
        var indexes = await SqliteObjectsAsync(db, "index", cancellationToken);
        // Columns first: the new indexes may cover them.
        await AddMissingSqliteColumnsAsync(db, tables, logger, cancellationToken);
        var statements = StatementEnd().Split(db.Database.GenerateCreateScript())
            .Select(s => s.Trim())
            .Where(s => StatementTarget().Match(s) is { Success: true } match
                && (!tables.Contains(match.Groups["table"].Value)
                    || match.Groups["index"].Success && !indexes.Contains(match.Groups["index"].Value)))
            .ToList();
        if (statements.Count == 0)
            return;

        // None of the context's tables yet: the other context created the file (a new install), nothing is updated.
        if (db.Model.GetEntityTypes().All(e => e.GetTableName() is not { } name || !tables.Contains(name)))
            logger.LogInformation("Creating the tables of {Context} in the SQLite database", db.GetType().Name);
        else
            // Indexing an existing table of a large library can take a while: say what is going on.
            logger.LogWarning("Updating the existing SQLite database: {Statements}", string.Join("; ", statements));
        await using var transaction = await db.Database.BeginTransactionAsync(cancellationToken);
        foreach (var statement in statements)
            await db.Database.ExecuteSqlRawAsync(statement, cancellationToken);
        await transaction.CommitAsync(cancellationToken);
    }

    private static async Task AddMissingSqliteColumnsAsync(DbContext db, HashSet<string> tables, ILogger logger, CancellationToken cancellationToken)
    {
        foreach (var entity in db.Model.GetEntityTypes())
        {
            var table = entity.GetTableName();
            if (table is null || !tables.Contains(table))
                continue;

            var columns = (await db.Database
                    // xinfo: table_info leaves out generated columns (Files.Pixels).
                    .SqlQuery<string>($"SELECT name AS Value FROM pragma_table_xinfo({table})")
                    .ToListAsync(cancellationToken))
                .ToHashSet(StringComparer.OrdinalIgnoreCase);
            var store = StoreObjectIdentifier.Table(table, entity.GetSchema());
            // Computed columns last: they read the others (Pixels = Width × Height).
            foreach (var property in entity.GetProperties().OrderBy(p => p.GetComputedColumnSql(store) is not null))
            {
                var column = property.GetColumnName(store);
                if (column is null || columns.Contains(column) || !property.IsColumnNullable(store))
                    continue;

                // A computed column fills itself for the existing rows; SQLite can only add a virtual one.
                var statement = $"ALTER TABLE \"{table}\" ADD COLUMN \"{column}\" {property.GetColumnType(store)}"
                    + (property.GetComputedColumnSql(store) is { } sql ? $" GENERATED ALWAYS AS ({sql}) VIRTUAL" : "");
                logger.LogWarning("Updating the existing SQLite database: {Statement}", statement);
                await db.Database.ExecuteSqlRawAsync(statement, cancellationToken);
                columns.Add(column);
            }
        }
    }

    private static async Task<HashSet<string>> SqliteObjectsAsync(DbContext db, string type, CancellationToken cancellationToken) =>
        (await db.Database
            .SqlQuery<string>($"SELECT name AS Value FROM sqlite_master WHERE type = {type}")
            .ToListAsync(cancellationToken))
        .ToHashSet(StringComparer.OrdinalIgnoreCase);

    [GeneratedRegex(@";\s*(?:\r?\n|$)")]
    private static partial Regex StatementEnd();

    /// <summary>Table a statement of the create script creates, indexes or fills, and the index it creates.</summary>
    [GeneratedRegex("""^(?:CREATE TABLE|CREATE (?:UNIQUE )?INDEX "(?<index>[^"]+)" ON|INSERT INTO) "(?<table>[^"]+)" """)]
    private static partial Regex StatementTarget();
}
