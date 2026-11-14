using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Otools.Net.Sfe.Core.Access;
using Otools.Net.Sfe.Core.Data;
using Otools.Net.Sfe.Tests.Infrastructure;

namespace Otools.Net.Sfe.Tests;

/// <summary>
/// The table of personal access tokens (<see cref="AccessToken"/>): added to an install made before it, one row per
/// lookup, and emptied with the accounts and the libraries its rows name.
/// </summary>
[TestClass]
public sealed class AccessTokenDataTests
{
    private TestLibrary _library = null!;

    public TestContext TestContext { get; set; } = null!;

    private CancellationToken Token => TestContext.CancellationToken;

    [TestInitialize]
    public void Initialize() => _library = new TestLibrary();

    [TestCleanup]
    public void Cleanup()
    {
        SqliteConnection.ClearAllPools();
        _library.Dispose();
    }

    /// <summary>
    /// An install of 1.85.0 has no such table: the update adds it to the SQLite file with its indexes, and the
    /// accounts are as before.
    /// </summary>
    [TestMethod]
    public async Task AnUpdatedInstallGetsTheTableAndKeepsItsAccounts()
    {
        await using (var factory = ApiClients.CreateFactory(_library))
            await ApiClients.SetUpAsync(factory, Token);
        await using (var connection = new SqliteConnection(_library.ConnectionString))
        {
            await connection.OpenAsync(Token);
            await using var command = connection.CreateCommand();
            command.CommandText = $"""
                DROP TABLE "AccessTokens";
                UPDATE "{AccessDbContext.ServerPropertiesTable}" SET "Value" = '1.85.0'
                WHERE "Key" = '{ServerProperty.VersionKey}';
                PRAGMA wal_checkpoint(TRUNCATE);
                """;
            await command.ExecuteNonQueryAsync(Token);
        }

        await using var updated = ApiClients.CreateFactory(_library);
        await ApiClients.MigrateAsync(updated, Token);
        await using var after = await updated
            .Services.GetRequiredService<IDbContextFactory<AccessDbContext>>()
            .CreateDbContextAsync(Token);

        var admin = await after.Users.SingleAsync(u => u.IsServerAdmin, Token);
        Assert.AreEqual(ApiClients.AdminName, admin.UserName);
        after.AccessTokens.Add(Row(admin.Id, "0123456789abcdef"));
        await after.SaveChangesAsync(Token);
        Assert.AreEqual(1, await after.AccessTokens.CountAsync(Token));
        var indexes = await after
            .Database.SqlQuery<string>($"SELECT name AS Value FROM sqlite_master WHERE type = 'index'")
            .ToListAsync(Token);
        CollectionAssert.IsSubsetOf(
            new[] { "IX_AccessTokens_Lookup", "IX_AccessTokens_UserId", "IX_AccessTokens_TenantId" },
            indexes
        );
    }

    [TestMethod]
    public async Task TwoTokensNeverShareALookup()
    {
        await using var world = await ConnectionWorld.CreateAsync(_library, Token);
        await using var db = world.Db();
        db.AccessTokens.AddRange(Row(world.Alice, "0123456789abcdef"), Row(world.Bob, "0123456789abcdef"));

        await Assert.ThrowsExactlyAsync<DbUpdateException>(() => db.SaveChangesAsync(Token));
    }

    [TestMethod]
    public async Task TokensGoWithTheirAccountAndTheirLibraryInTheDatabase()
    {
        await using var world = await ConnectionWorld.CreateAsync(_library, Token);
        var accounts = world.Services.GetRequiredService<AccessService>();
        await using (var db = world.Db())
        {
            db.AccessTokens.AddRange(
                Row(world.Alice, "000000000000000a"),
                Row(world.Bob, "000000000000000b"),
                Row(world.Carol, "000000000000000c", world.OtherTenant)
            );
            await db.SaveChangesAsync(Token);
        }

        Assert.AreEqual(AccessStatus.Ok, (await accounts.DeleteUserAsync(world.Bob, Token)).Status);
        Assert.AreEqual(AccessStatus.Ok, (await accounts.DeleteTenantAsync(world.OtherTenant, Token)).Status);

        await using var after = world.Db();
        Assert.AreEqual(world.Alice, (await after.AccessTokens.SingleAsync(Token)).UserId);
        Assert.IsTrue(await after.Users.AnyAsync(u => u.Id == world.Carol, Token));
    }

    private static AccessToken Row(int userId, string lookup, int? tenantId = null) =>
        new()
        {
            UserId = userId,
            Name = "Backup script",
            Lookup = lookup,
            Hash = new byte[AccessToken.HashLength],
            Prefix = $"sfe_{lookup}_abcd",
            TenantId = tenantId,
            Kind = AccessTokenKind.Personal,
            Created = DateTime.UtcNow,
        };
}
