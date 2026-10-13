using System.Globalization;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Storage;
using Sfe.Core.Data;

namespace Sfe.Core.Upgrades;

/// <param name="Exists">The database exists and has tables: an install, set up by some version of SFE.</param>
/// <param name="Version">The version that last set it up or updated it; null when it predates the record (before 1.37.0).</param>
public sealed record InstalledVersionInfo(bool Exists, string? Version);

/// <summary>
/// The version of SFE the install's databases and settings belong to (<see cref="ServerProperty.VersionKey"/>, in the
/// accounts' database). Read before anything else at start-up, without creating or changing anything: a newer
/// version must not touch an install until its update is confirmed.
/// </summary>
public sealed class InstalledVersion(IDbContextFactory<AccessDbContext> dbFactory)
{
    public async Task<InstalledVersionInfo> ReadAsync(CancellationToken cancellationToken = default)
    {
        await using var db = await dbFactory.CreateDbContextAsync(cancellationToken);
        // Exists: SQLite checks the file without creating it; SQL Server asks the server for the database.
        var creator = db.GetService<IRelationalDatabaseCreator>();
        if (!await creator.ExistsAsync(cancellationToken) || !await creator.HasTablesAsync(cancellationToken))
            return new InstalledVersionInfo(false, null);
        if (!await HasTableAsync(db, AccessDbContext.ServerPropertiesTable, cancellationToken))
            return new InstalledVersionInfo(true, null);

        // Only the key and the value: the rest of the model may be ahead of this database.
        var version = await db.ServerProperties.AsNoTracking()
            .Where(p => p.Key == ServerProperty.VersionKey)
            .Select(p => p.Value)
            .FirstOrDefaultAsync(cancellationToken);
        return new InstalledVersionInfo(true, version);
    }

    /// <summary>Records that the install now belongs to <paramref name="version"/> (its schema is up to date).</summary>
    public async Task RecordAsync(string version, CancellationToken cancellationToken = default)
    {
        await using var db = await dbFactory.CreateDbContextAsync(cancellationToken);
        await SetAsync(db, ServerProperty.VersionKey, version, cancellationToken);
        await SetAsync(db, ServerProperty.UpdatedKey, DateTime.UtcNow.ToString("O", CultureInfo.InvariantCulture), cancellationToken);
        await db.SaveChangesAsync(cancellationToken);
    }

    private static async Task SetAsync(AccessDbContext db, string key, string value, CancellationToken cancellationToken)
    {
        if (await db.ServerProperties.FindAsync([key], cancellationToken) is { } existing)
            existing.Value = value;
        else
            db.ServerProperties.Add(new ServerProperty { Key = key, Value = value });
    }

    /// <summary>The database has <paramref name="table"/> (in schema SFE on SQL Server): asked before querying a table an older install may lack.</summary>
    public static async Task<bool> HasTableAsync(DbContext db, string table, CancellationToken cancellationToken = default)
    {
        var query = db.Database.IsSqlServer()
            ? db.Database.SqlQuery<int>($"SELECT COUNT(*) AS Value FROM INFORMATION_SCHEMA.TABLES WHERE TABLE_SCHEMA = {SfeDbContext.Schema} AND TABLE_NAME = {table}")
            : db.Database.SqlQuery<int>($"SELECT COUNT(*) AS Value FROM sqlite_master WHERE type = 'table' AND name = {table}");
        return await query.SingleAsync(cancellationToken) > 0;
    }
}
