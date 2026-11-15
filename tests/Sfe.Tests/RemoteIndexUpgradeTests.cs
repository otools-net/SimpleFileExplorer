using Microsoft.Data.SqlClient;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using Otools.Net.Sfe.Core.Data;
using Otools.Net.Sfe.Core.Options;
using Otools.Net.Sfe.Tests.Infrastructure;

namespace Otools.Net.Sfe.Tests;

/// <summary>
/// The tables of the connections' index (<see cref="RemoteFile"/>…) added to a library's index made before them: on
/// SQLite by <c>DatabaseSchema</c> (tables and indexes), on SQL Server by the migrations <c>ConnectionIndex</c> and
/// <c>ConnectionIndexListings</c> (LocalDB, when the machine has it), the existing rows kept; the listings' indexes
/// also added to an index that has the tables already.
/// </summary>
[TestClass]
public sealed class RemoteIndexUpgradeTests
{
    /// <summary>The migration shipped before the connections' index.</summary>
    private const string Previous = "20261022204629_AddFolderDefaultTags";

    /// <summary>
    /// The indexes of the listings read from the index (migration <c>ConnectionIndexListings</c>); first, since
    /// <see cref="Indexes"/> holds them.
    /// </summary>
    private static readonly string[] ListingIndexes =
    [
        "IX_RemoteFiles_ConnectionId_DirectoryId_NameKey_FullName_Removed",
        "IX_RemoteFiles_ConnectionId_DirectoryId_LastWriteTime_NameKey_FullName",
        "IX_RemoteFiles_ConnectionId_DirectoryId_Size_NameKey_FullName",
        "IX_RemoteFiles_ConnectionId_DirectoryId_Extension_NameKey_FullName",
        "IX_RemoteDirectories_ConnectionId_ParentId_NameKey_FullName_Removed",
    ];

    private static readonly string[] Indexes =
    [
        "IX_RemoteFiles_ConnectionId_FullName",
        "IX_RemoteFiles_ConnectionId_ProviderId",
        "IX_RemoteFiles_ConnectionId_DirectoryId",
        "IX_RemoteFiles_ConnectionId_Name",
        "IX_RemoteFiles_ConnectionId_Extension",
        "IX_RemoteFiles_ConnectionId_Size",
        "IX_RemoteFiles_ConnectionId_LastWriteTime",
        "IX_RemoteFiles_ConnectionId_Duration",
        "IX_RemoteFiles_ConnectionId_Pixels",
        "IX_RemoteFiles_ConnectionId_Removed",
        "IX_RemoteFileTags_TagId",
        "IX_RemoteDirectories_ConnectionId_FullName",
        "IX_RemoteDirectories_ConnectionId_Pending",
        "IX_RemoteDirectories_ConnectionId_Removed",
        .. ListingIndexes,
    ];

    public TestContext TestContext { get; set; } = null!;

    private CancellationToken Token => TestContext.CancellationToken;

    [TestMethod]
    public async Task AnExistingSqliteIndexGetsTheTablesAndTheirIndexes()
    {
        using var library = new TestLibrary();
        await using (var services = await CoreServices.CreateAsync(library))
        {
            await using var db = Db(services);
            // The index as 1.85 made it, with a folder of the library in it.
            foreach (var table in new[] { "RemoteFileTags", "RemoteFiles", "RemoteDirectories", "RemoteIndexes" })
                await db.Database.ExecuteSqlRawAsync($"DROP TABLE \"{table}\"", Token);
            db.Directories.Add(new DirectoryEntry { FullName = library.Root, Name = "root" });
            await db.SaveChangesAsync(Token);
        }

        await using var updated = await CoreServices.CreateAsync(library);
        await using var after = Db(updated);
        var indexes = await after
            .Database.SqlQuery<string>($"SELECT name AS Value FROM sqlite_master WHERE type = 'index'")
            .ToListAsync(Token);
        CollectionAssert.IsSubsetOf(Indexes, indexes);
        Assert.AreEqual(1, await after.Directories.CountAsync(Token));
        await WriteRowsAsync(after);
        SqliteConnection.ClearAllPools();
    }

    [TestMethod]
    public async Task AnSqliteIndexWithTheTablesGetsTheNameKeysAndListingIndexesAndKeepsItsRows()
    {
        using var library = new TestLibrary();
        await using (var services = await CoreServices.CreateAsync(library))
        {
            await using var db = Db(services);
            // The connections' tables as the first step of the index made them, with a row.
            foreach (var index in ListingIndexes)
                await db.Database.ExecuteSqlRawAsync($"DROP INDEX \"{index}\"", Token);
            foreach (var table in new[] { "RemoteFiles", "RemoteDirectories" })
                await db.Database.ExecuteSqlRawAsync($"ALTER TABLE \"{table}\" DROP COLUMN \"NameKey\"", Token);
            await db.Database.ExecuteSqlRawAsync(
                "INSERT INTO \"RemoteIndexes\" (\"ConnectionId\", \"RootPath\", \"Phase\", \"Pass\", \"Files\", "
                    + "\"Folders\", \"TooLong\", \"Created\") "
                    + "VALUES (7, '/kept', 'Following', 1, 0, 0, 0, '2026-11-01')",
                Token
            );
            await db.Database.ExecuteSqlRawAsync(
                "INSERT INTO \"RemoteDirectories\" (\"ConnectionId\", \"FullName\", \"DisplayPath\", \"Name\", "
                    + "\"Listed\", \"Pending\", \"Pass\", \"Removed\") VALUES (7, '', '', '', 1, 0, 1, 0)",
                Token
            );
        }

        await using var updated = await CoreServices.CreateAsync(library);
        await using var after = Db(updated);
        var indexes = await after
            .Database.SqlQuery<string>($"SELECT name AS Value FROM sqlite_master WHERE type = 'index'")
            .ToListAsync(Token);
        CollectionAssert.IsSubsetOf(ListingIndexes, indexes);
        Assert.AreEqual("/kept", (await after.RemoteIndexes.SingleAsync(Token)).RootPath);
        Assert.IsNull((await after.RemoteDirectories.SingleAsync(Token)).NameKey, "filled by the next sync");
        SqliteConnection.ClearAllPools();
    }

    [TestMethod]
    public async Task TheSqlServerMigrationAddsTheTablesToAnExistingIndex()
    {
        var connectionString = new SqlConnectionStringBuilder
        {
            DataSource = @"(localdb)\MSSQLLocalDB",
            InitialCatalog = $"sfe_test_{Guid.NewGuid():N}",
            IntegratedSecurity = true,
            TrustServerCertificate = true,
            ConnectTimeout = 60,
        }.ConnectionString;
        if (!OperatingSystem.IsWindows() || !await LocalDbAnswersAsync(connectionString))
            Assert.Inconclusive("SQL Server LocalDB is not on this machine.");

        var options = SfeDatabase.Configure(
            new DbContextOptionsBuilder<SfeDbContext>(),
            DatabaseProvider.SqlServer,
            connectionString
        );
        await using var db = new SfeDbContext((DbContextOptions<SfeDbContext>)options.Options);
        try
        {
            await db.GetService<IMigrator>().MigrateAsync(Previous, Token);
            db.Directories.Add(new DirectoryEntry { FullName = @"C:\Library", Name = "Library" });
            await db.SaveChangesAsync(Token);
            db.ChangeTracker.Clear();

            await db.Database.MigrateAsync(Token);

            Assert.AreEqual(1, await db.Directories.CountAsync(Token));
            var indexes = await db
                .Database.SqlQuery<string>($"SELECT name AS Value FROM sys.indexes WHERE name LIKE 'IX_Remote%'")
                .ToListAsync(Token);
            CollectionAssert.IsSubsetOf(Indexes, indexes);
            await WriteRowsAsync(db);
        }
        finally
        {
            await db.Database.EnsureDeletedAsync(CancellationToken.None);
        }
    }

    /// <summary>The new tables take rows: a file with a tag, the longest path kept, the computed pixels.</summary>
    private async Task WriteRowsAsync(SfeDbContext db)
    {
        var root = new RemoteDirectory
        {
            ConnectionId = 1,
            FullName = "",
            DisplayPath = "",
            Name = "",
        };
        var file = new RemoteFile
        {
            ConnectionId = 1,
            Directory = root,
            ProviderId = "id:1",
            FullName = new string('a', SfeDbContext.MaxPathLength),
            DisplayPath = new string('A', SfeDbContext.MaxPathLength),
            Name = "A",
            Width = 4000,
            Height = 3000,
        };
        file.TagLinks.Add(new RemoteFileTag { TagId = Core.Tagging.BuiltInTags.Favorite });
        db.RemoteFiles.Add(file);
        db.RemoteIndexes.Add(new RemoteIndexState { ConnectionId = 1, ChangeCursor = new string('c', 5000) });
        await db.SaveChangesAsync(Token);
        db.ChangeTracker.Clear();

        var read = await db.RemoteFiles.Include(f => f.TagLinks).SingleAsync(Token);
        Assert.AreEqual(12_000_000L, read.Pixels);
        Assert.HasCount(1, read.TagLinks);
        await db.Database.ExecuteSqlRawAsync("DELETE FROM " + Table(db, "RemoteDirectories"), Token);
        Assert.AreEqual(0, await db.RemoteFileTags.CountAsync(Token), "A folder's files and their tags go with it.");
    }

    private static string Table(SfeDbContext db, string name) =>
        db.Database.IsSqlServer() ? $"[{SfeDbContext.Schema}].[{name}]" : $"\"{name}\"";

    private static SfeDbContext Db(CoreServices services) =>
        services.Get<IDbContextFactory<SfeDbContext>>().CreateDbContext();

    private async Task<bool> LocalDbAnswersAsync(string connectionString)
    {
        var master = new SqlConnectionStringBuilder(connectionString) { InitialCatalog = "master" }.ConnectionString;
        try
        {
            await using var connection = new SqlConnection(master);
            await connection.OpenAsync(Token);
            return true;
        }
        catch (SqlException)
        {
            return false;
        }
    }
}
