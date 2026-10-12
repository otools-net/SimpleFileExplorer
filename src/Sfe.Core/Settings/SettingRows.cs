using System.Collections.Concurrent;
using System.Text.Json.Nodes;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Sfe.Core.Access;
using Sfe.Core.Data;

namespace Sfe.Core.Settings;

/// <param name="Locked">The library's users cannot change it for themselves.</param>
public sealed record StoredSetting(JsonNode? Value, bool Locked);

/// <summary>
/// The settings of libraries and users, one row per setting (<see cref="TenantSetting"/>, <see cref="UserSetting"/>).
/// A library's rows are read while its options are built, which is synchronous: they are cached until the next
/// change of the accounts or settings.
/// </summary>
public sealed class SettingRows(IDbContextFactory<AccessDbContext> dbFactory, AccessChanges changes, ILogger<SettingRows> logger)
{
    private static readonly IReadOnlyDictionary<string, StoredSetting> NoRows = new Dictionary<string, StoredSetting>();

    private readonly ConcurrentDictionary<int, (long Version, IReadOnlyDictionary<string, StoredSetting> Rows)> _tenants = new();
    private (long Version, int? Id)? _defaultTenant;

    /// <summary>The first library (the configuration's), whose settings apply where no library is named.</summary>
    public int? DefaultTenantId()
    {
        var version = changes.Version;
        if (_defaultTenant is { } cached && cached.Version == version)
            return cached.Id;

        try
        {
            using var db = dbFactory.CreateDbContext();
            var id = db.Tenants.AsNoTracking().Where(t => t.IsDefault).Select(t => (int?)t.Id).FirstOrDefault();
            _defaultTenant = (version, id);
            return id;
        }
        catch (Exception ex)
        {
            // Before the database exists (first start): the library has no settings of its own yet.
            logger.LogDebug(ex, "Cannot read the libraries yet");
            return null;
        }
    }

    public IReadOnlyDictionary<string, StoredSetting> ForTenant(int tenantId)
    {
        var version = changes.Version;
        if (_tenants.TryGetValue(tenantId, out var cached) && cached.Version == version)
            return cached.Rows;

        try
        {
            using var db = dbFactory.CreateDbContext();
            var rows = db.TenantSettings.AsNoTracking().Where(s => s.TenantId == tenantId).ToList()
                .ToDictionary(s => s.Key, s => new StoredSetting(Parse(s.Value), s.Locked), StringComparer.Ordinal);
            _tenants[tenantId] = (version, rows);
            return rows;
        }
        catch (Exception ex)
        {
            logger.LogDebug(ex, "Cannot read the settings of library {Tenant} yet", tenantId);
            return NoRows;
        }
    }

    /// <summary>Replaces a library's settings; its services read their options again.</summary>
    public async Task SaveTenantAsync(int tenantId, IReadOnlyDictionary<string, StoredSetting> rows, CancellationToken cancellationToken = default)
    {
        await using (var db = await dbFactory.CreateDbContextAsync(cancellationToken))
        {
            var strategy = db.Database.CreateExecutionStrategy();
            await strategy.ExecuteAsync(async () =>
            {
                db.ChangeTracker.Clear();
                await using var transaction = await db.Database.BeginTransactionAsync(cancellationToken);
                await db.TenantSettings.Where(s => s.TenantId == tenantId).ExecuteDeleteAsync(cancellationToken);
                db.TenantSettings.AddRange(rows.Select(r => new TenantSetting
                {
                    TenantId = tenantId,
                    Key = r.Key,
                    Value = r.Value.Value?.ToJsonString() ?? "null",
                    Locked = r.Value.Locked,
                }));
                await db.SaveChangesAsync(cancellationToken);
                await transaction.CommitAsync(cancellationToken);
            });
        }

        changes.SettingsChanged(tenantId);
    }

    public async Task<IReadOnlyDictionary<string, JsonNode?>> ForUserAsync(int userId, CancellationToken cancellationToken = default)
    {
        await using var db = await dbFactory.CreateDbContextAsync(cancellationToken);
        return (await db.UserSettings.AsNoTracking().Where(s => s.UserId == userId).ToListAsync(cancellationToken))
            .ToDictionary(s => s.Key, s => Parse(s.Value), StringComparer.Ordinal);
    }

    public async Task SaveUserAsync(int userId, IReadOnlyDictionary<string, JsonNode?> rows, CancellationToken cancellationToken = default)
    {
        await using var db = await dbFactory.CreateDbContextAsync(cancellationToken);
        var strategy = db.Database.CreateExecutionStrategy();
        await strategy.ExecuteAsync(async () =>
        {
            db.ChangeTracker.Clear();
            await using var transaction = await db.Database.BeginTransactionAsync(cancellationToken);
            await db.UserSettings.Where(s => s.UserId == userId).ExecuteDeleteAsync(cancellationToken);
            db.UserSettings.AddRange(rows.Select(r => new UserSetting { UserId = userId, Key = r.Key, Value = r.Value?.ToJsonString() ?? "null" }));
            await db.SaveChangesAsync(cancellationToken);
            await transaction.CommitAsync(cancellationToken);
        });
    }

    private static JsonNode? Parse(string json)
    {
        try
        {
            return JsonNode.Parse(json);
        }
        catch (System.Text.Json.JsonException)
        {
            return null;
        }
    }
}
