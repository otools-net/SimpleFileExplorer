using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Sfe.Core.Access;
using Sfe.Core.Connections;
using Sfe.Core.Data;
using Sfe.Tests.Infrastructure;

namespace Sfe.Tests;

/// <summary>
/// The tables of connections (<see cref="Connection"/>, <see cref="ConnectionTenant"/>,
/// <see cref="ConnectionGroup"/>): added to an install made before them, and emptied with the accounts, groups and
/// libraries their rows name.
/// </summary>
[TestClass]
public sealed class ConnectionDataTests
{
    private TestLibrary _library = null!;

    public TestContext TestContext { get; set; } = null!;

    private CancellationToken Cancel => TestContext.CancellationToken;

    [TestInitialize]
    public void Initialize() => _library = new TestLibrary();

    [TestCleanup]
    public void Cleanup()
    {
        SqliteConnection.ClearAllPools();
        _library.Dispose();
    }

    /// <summary>
    /// An install of 1.85.0 has none of the tables: the update adds them to the SQLite file with their indexes, and
    /// the accounts and libraries are as before.
    /// </summary>
    [TestMethod]
    public async Task AnUpdatedInstallGetsTheTables()
    {
        await using (var factory = ApiClients.CreateFactory(_library))
            await ApiClients.SetUpAsync(factory, Cancel);
        await using (var connection = new SqliteConnection(_library.ConnectionString))
        {
            await connection.OpenAsync(Cancel);
            await using var command = connection.CreateCommand();
            command.CommandText = $"""
                DROP TABLE "ConnectionGroups";
                DROP TABLE "ConnectionTenants";
                DROP TABLE "Connections";
                UPDATE "{AccessDbContext.ServerPropertiesTable}" SET "Value" = '1.85.0'
                WHERE "Key" = '{ServerProperty.VersionKey}';
                PRAGMA wal_checkpoint(TRUNCATE);
                """;
            await command.ExecuteNonQueryAsync(Cancel);
        }

        await using var updated = ApiClients.CreateFactory(_library);
        await ApiClients.MigrateAsync(updated, Cancel);
        var world = await WorldAsync(updated);
        await using var db = await DbAsync(updated);

        db.Connections.Add(Server("Shared", world.Admin));
        await db.SaveChangesAsync(Cancel);

        Assert.AreEqual(1, await db.Connections.CountAsync(Cancel));
        Assert.HasCount(1, await updated.Services.GetRequiredService<AccessService>().GetTenantsAsync(Cancel));
        var indexes = await db
            .Database.SqlQuery<string>($"SELECT name AS Value FROM sqlite_master WHERE type = 'index'")
            .ToListAsync(Cancel);
        CollectionAssert.IsSubsetOf(
            new[]
            {
                "IX_Connections_OwnerId",
                "IX_Connections_TenantId",
                "IX_ConnectionTenants_TenantId",
                "IX_ConnectionGroups_GroupId",
            },
            indexes
        );
    }

    [TestMethod]
    public async Task RowsGoWithTheirAccountGroupAndLibrary()
    {
        await using var factory = ApiClients.CreateFactory(_library);
        await ApiClients.SetUpAsync(factory, Cancel);
        var accounts = factory.Services.GetRequiredService<AccessService>();
        var world = await WorldAsync(factory);
        var bob = (await accounts.CreateUserAsync(new UserInput("bob", Password: "bob-password"), null, Cancel)).Value!;
        var other = (await accounts.CreateTenantAsync(new TenantInput("Other"), _library.Root, Cancel)).Value!;
        var otherUsers = (await accounts.GetBuiltInGroupAsync(other.Id, BuiltInGroup.Users, Cancel))!.Id;
        var team = (await accounts.CreateGroupAsync(world.Tenant, new GroupInput("Team"), false, Cancel)).Value!;

        await using (var db = await DbAsync(factory))
        {
            var shared = Server("Shared", world.Admin);
            shared.Tenants.Add(new ConnectionTenant { TenantId = other.Id });
            shared.Groups.Add(new ConnectionGroup { GroupId = otherUsers, Level = FolderLevel.Read });
            shared.Groups.Add(new ConnectionGroup { GroupId = team.Id, Level = FolderLevel.Write });
            db.Connections.AddRange(
                shared,
                new Connection
                {
                    Kind = FakeProvider.FakeKind,
                    Name = "Bob's",
                    Level = ConnectionLevel.Account,
                    OwnerId = bob.Id,
                },
                new Connection
                {
                    Kind = FakeProvider.FakeKind,
                    Name = "Other's",
                    Level = ConnectionLevel.Library,
                    TenantId = other.Id,
                    // Made by bob: it stays when bob goes.
                    CreatedById = bob.Id,
                }
            );
            await db.SaveChangesAsync(Cancel);
        }

        Assert.AreEqual(AccessStatus.Ok, (await accounts.DeleteUserAsync(bob.Id, Cancel)).Status);
        CollectionAssert.AreEquivalent(new[] { "Shared", "Other's" }, await NamesAsync(factory));

        Assert.AreEqual(AccessStatus.Ok, (await accounts.DeleteGroupAsync(world.Tenant, team.Id, Cancel)).Status);
        await using (var db = await DbAsync(factory))
            Assert.AreEqual(otherUsers, (await db.ConnectionGroups.SingleAsync(Cancel)).GroupId);

        Assert.AreEqual(AccessStatus.Ok, (await accounts.DeleteTenantAsync(other.Id, Cancel)).Status);
        CollectionAssert.AreEquivalent(new[] { "Shared" }, await NamesAsync(factory));
        await using (var db = await DbAsync(factory))
        {
            Assert.IsFalse(await db.ConnectionGroups.AnyAsync(Cancel));
            Assert.IsFalse(await db.ConnectionTenants.AnyAsync(Cancel));
        }
    }

    /// <summary>
    /// The database does not cascade from a connection to who uses it (two cascade paths on SQL Server): loaded
    /// with it, the rows are deleted by EF in the same save.
    /// </summary>
    [TestMethod]
    public async Task AConnectionLoadedWithItsRowsIsRemovedWithThem()
    {
        await using var factory = ApiClients.CreateFactory(_library);
        await ApiClients.SetUpAsync(factory, Cancel);
        var world = await WorldAsync(factory);
        var accounts = factory.Services.GetRequiredService<AccessService>();
        var users = (await accounts.GetBuiltInGroupAsync(world.Tenant, BuiltInGroup.Users, Cancel))!.Id;
        await using (var db = await DbAsync(factory))
        {
            var shared = Server("Shared", world.Admin);
            shared.Tenants.Add(new ConnectionTenant { TenantId = world.Tenant });
            shared.Groups.Add(new ConnectionGroup { GroupId = users, Level = FolderLevel.Read });
            db.Connections.Add(shared);
            await db.SaveChangesAsync(Cancel);
        }

        await using (var db = await DbAsync(factory))
        {
            var bare = await db.Connections.SingleAsync(Cancel);
            db.Connections.Remove(bare);
            await Assert.ThrowsExactlyAsync<DbUpdateException>(() => db.SaveChangesAsync(Cancel));
        }
        await using (var db = await DbAsync(factory))
        {
            var loaded = await db.Connections.Include(c => c.Tenants).Include(c => c.Groups).SingleAsync(Cancel);
            db.Connections.Remove(loaded);
            await db.SaveChangesAsync(Cancel);

            Assert.IsFalse(await db.ConnectionGroups.AnyAsync(Cancel));
            Assert.IsFalse(await db.ConnectionTenants.AnyAsync(Cancel));
        }
    }

    [TestMethod]
    public async Task ALevelNamesItsOwnerOrItsLibraryAndNothingElse()
    {
        await using var factory = ApiClients.CreateFactory(_library);
        await ApiClients.SetUpAsync(factory, Cancel);
        var world = await WorldAsync(factory);
        Connection[] wrong =
        [
            new() { Level = ConnectionLevel.Account },
            new()
            {
                Level = ConnectionLevel.Account,
                OwnerId = world.Admin,
                TenantId = world.Tenant,
            },
            new() { Level = ConnectionLevel.Library },
            new()
            {
                Level = ConnectionLevel.Library,
                TenantId = world.Tenant,
                OwnerId = world.Admin,
            },
            new() { Level = ConnectionLevel.Server, OwnerId = world.Admin },
            new() { Level = ConnectionLevel.Server, TenantId = world.Tenant },
        ];

        foreach (var connection in wrong)
        {
            connection.Kind = FakeProvider.FakeKind;
            connection.Name = "Wrong";
            await using var db = await DbAsync(factory);
            db.Connections.Add(connection);
            await Assert.ThrowsExactlyAsync<DbUpdateException>(() => db.SaveChangesAsync(Cancel));
        }
    }

    private static Connection Server(string name, int createdBy) =>
        new()
        {
            Kind = FakeProvider.FakeKind,
            Name = name,
            Level = ConnectionLevel.Server,
            Credentials = "protected",
            Created = DateTime.UtcNow,
            CreatedById = createdBy,
        };

    private async Task<(int Tenant, int Admin)> WorldAsync(WebApplicationFactory<Program> factory)
    {
        var accounts = factory.Services.GetRequiredService<AccessService>();
        return (
            (await accounts.GetTenantsAsync(Cancel)).Single(t => t.IsDefault).Id,
            (await accounts.GetServerAdminIdsAsync(Cancel)).Single()
        );
    }

    private Task<AccessDbContext> DbAsync(WebApplicationFactory<Program> factory) =>
        factory.Services.GetRequiredService<IDbContextFactory<AccessDbContext>>().CreateDbContextAsync(Cancel);

    private async Task<string[]> NamesAsync(WebApplicationFactory<Program> factory)
    {
        await using var db = await DbAsync(factory);
        return await db.Connections.Select(c => c.Name).ToArrayAsync(Cancel);
    }
}
