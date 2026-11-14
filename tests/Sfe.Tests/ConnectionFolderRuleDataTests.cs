using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Sfe.Core.Access;
using Sfe.Core.Connections;
using Sfe.Core.Data;
using Sfe.Tests.Infrastructure;

namespace Sfe.Tests;

/// <summary>
/// The table of connections' folder rules (<see cref="ConnectionFolderRule"/>): added to an install made before it,
/// one principal per rule, and emptied with the libraries, accounts, groups and connections its rows name.
/// </summary>
[TestClass]
public sealed class ConnectionFolderRuleDataTests
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
    /// connections and their groups are as before.
    /// </summary>
    [TestMethod]
    public async Task AnUpdatedInstallGetsTheTableAndKeepsItsConnections()
    {
        await using (var factory = ApiClients.CreateFactory(_library))
        {
            await ApiClients.SetUpAsync(factory, Token);
            await using var db = await DbAsync(factory);
            var admin = await db.Users.Where(u => u.IsServerAdmin).Select(u => u.Id).SingleAsync(Token);
            var users = await db
                .Groups.Where(g => g.BuiltIn == BuiltInGroup.Users)
                .Select(g => g.Id)
                .SingleAsync(Token);
            var connection = Library(await db.Tenants.Select(t => t.Id).SingleAsync(Token), admin);
            connection.Groups.Add(new ConnectionGroup { GroupId = users, Level = FolderLevel.Write });
            db.Connections.Add(connection);
            await db.SaveChangesAsync(Token);
        }
        await using (var connection = new SqliteConnection(_library.ConnectionString))
        {
            await connection.OpenAsync(Token);
            await using var command = connection.CreateCommand();
            command.CommandText = $"""
                DROP TABLE "ConnectionFolderRules";
                UPDATE "{AccessDbContext.ServerPropertiesTable}" SET "Value" = '1.85.0'
                WHERE "Key" = '{ServerProperty.VersionKey}';
                PRAGMA wal_checkpoint(TRUNCATE);
                """;
            await command.ExecuteNonQueryAsync(Token);
        }

        await using var updated = ApiClients.CreateFactory(_library);
        await ApiClients.MigrateAsync(updated, Token);
        await using var after = await DbAsync(updated);

        var kept = await after.Connections.Include(c => c.Groups).SingleAsync(Token);
        Assert.AreEqual(("Shared", FolderLevel.Write), (kept.Name, kept.Groups.Single().Level));
        after.ConnectionFolderRules.Add(Rule(kept, kept.Groups.Single().GroupId, null));
        await after.SaveChangesAsync(Token);
        Assert.AreEqual(1, await after.ConnectionFolderRules.CountAsync(Token));
        var indexes = await after
            .Database.SqlQuery<string>($"SELECT name AS Value FROM sqlite_master WHERE type = 'index'")
            .ToListAsync(Token);
        CollectionAssert.IsSubsetOf(
            new[]
            {
                "IX_ConnectionFolderRules_TenantId_ConnectionId",
                "IX_ConnectionFolderRules_ConnectionId",
                "IX_ConnectionFolderRules_GroupId",
                "IX_ConnectionFolderRules_UserId",
            },
            indexes
        );
    }

    [TestMethod]
    public async Task ARuleNamesAGroupOrAnAccountNeverBothNorNeither()
    {
        await using var world = await ConnectionWorld.CreateAsync(_library, Token);
        var connection = await AddAsync(world, Library(world.Tenant, world.Admin));

        foreach (var (group, user) in new (int?, int?)[] { (null, null), (world.Users, world.Alice) })
        {
            await using var db = world.Db();
            db.ConnectionFolderRules.Add(Rule(connection, group, user));
            await Assert.ThrowsExactlyAsync<DbUpdateException>(() => db.SaveChangesAsync(Token));
        }
    }

    [TestMethod]
    public async Task RulesGoWithTheirLibraryAndTheirAccountInTheDatabase()
    {
        await using var world = await ConnectionWorld.CreateAsync(_library, Token);
        var accounts = world.Services.GetRequiredService<AccessService>();
        var server = Server(world.Admin);
        server.Tenants.Add(new ConnectionTenant { TenantId = world.OtherTenant });
        var connection = await AddAsync(world, server);
        await AddRulesAsync(
            world,
            Rule(connection, world.Users, null, world.Tenant),
            Rule(connection, null, world.Bob, world.Tenant),
            Rule(connection, world.OtherUsers, null, world.OtherTenant)
        );

        Assert.AreEqual(AccessStatus.Ok, (await accounts.DeleteUserAsync(world.Bob, Token)).Status);
        Assert.AreEqual(AccessStatus.Ok, (await accounts.DeleteTenantAsync(world.OtherTenant, Token)).Status);

        await using var db = world.Db();
        var left = await db.ConnectionFolderRules.SingleAsync(Token);
        Assert.AreEqual((world.Users, world.Tenant), (left.GroupId, left.TenantId));
        Assert.AreEqual(1, await db.Connections.CountAsync(Token));
    }

    /// <summary>
    /// No database cascade from a group or a connection (two cascade paths on SQL Server): a bare delete is refused,
    /// the services that remove them delete the rules first.
    /// </summary>
    [TestMethod]
    public async Task RulesGoWithTheirGroupAndTheirConnectionThroughTheirServices()
    {
        await using var world = await ConnectionWorld.CreateAsync(_library, Token);
        var accounts = world.Services.GetRequiredService<AccessService>();
        var connection = await AddAsync(world, Library(world.Tenant, world.Admin));
        await AddRulesAsync(world, Rule(connection, world.Team, null), Rule(connection, world.Users, null));

        await using (var db = world.Db())
        {
            db.Connections.Remove(await db.Connections.SingleAsync(Token));
            await Assert.ThrowsExactlyAsync<DbUpdateException>(() => db.SaveChangesAsync(Token));
        }

        Assert.AreEqual(AccessStatus.Ok, (await accounts.DeleteGroupAsync(world.Tenant, world.Team, Token)).Status);
        await using (var db = world.Db())
            Assert.AreEqual(world.Users, (await db.ConnectionFolderRules.SingleAsync(Token)).GroupId);

        Assert.AreEqual(AccessStatus.Ok, await world.Connections.RemoveAsync(connection.Id, world.Admin, Token));
        await using (var db = world.Db())
            Assert.IsFalse(await db.ConnectionFolderRules.AnyAsync(Token));
    }

    private static Connection Library(int tenantId, int createdBy) =>
        new()
        {
            Kind = FakeProvider.FakeKind,
            Name = "Shared",
            Level = ConnectionLevel.Library,
            TenantId = tenantId,
            Created = DateTime.UtcNow,
            CreatedById = createdBy,
        };

    private static Connection Server(int createdBy) =>
        new()
        {
            Kind = FakeProvider.FakeKind,
            Name = "Server",
            Level = ConnectionLevel.Server,
            Created = DateTime.UtcNow,
            CreatedById = createdBy,
        };

    private static ConnectionFolderRule Rule(Connection connection, int? groupId, int? userId, int? tenantId = null) =>
        new()
        {
            ConnectionId = connection.Id,
            TenantId = tenantId ?? connection.TenantId!.Value,
            Path = "Private",
            GroupId = groupId,
            UserId = userId,
            Level = FolderLevel.Hidden,
            ItemId = "id:private",
            Created = DateTime.UtcNow,
        };

    private async Task<Connection> AddAsync(ConnectionWorld world, Connection connection)
    {
        await using var db = world.Db();
        db.Connections.Add(connection);
        await db.SaveChangesAsync(Token);
        return connection;
    }

    private async Task AddRulesAsync(ConnectionWorld world, params ConnectionFolderRule[] rules)
    {
        await using var db = world.Db();
        db.ConnectionFolderRules.AddRange(rules);
        await db.SaveChangesAsync(Token);
    }

    private Task<AccessDbContext> DbAsync(Microsoft.AspNetCore.Mvc.Testing.WebApplicationFactory<Program> factory) =>
        factory.Services.GetRequiredService<IDbContextFactory<AccessDbContext>>().CreateDbContextAsync(Token);
}
