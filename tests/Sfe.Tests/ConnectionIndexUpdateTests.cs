using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Otools.Net.Sfe.Core.Connections;
using Otools.Net.Sfe.Core.Connections.Indexing;
using Otools.Net.Sfe.Core.Data;
using Otools.Net.Sfe.Core.Tagging;
using Otools.Net.Sfe.Tests.Infrastructure;

namespace Otools.Net.Sfe.Tests;

/// <summary>
/// Changes made through SFE written at once into a library's index of a connection
/// (<see cref="ConnectionIndexUpdates"/>), and a connection's rows dropped in batches
/// (<see cref="RemoteIndexCleanup"/>).
/// </summary>
[TestClass]
public sealed class ConnectionIndexUpdateTests
{
    private const int Id = 3;

    private readonly FakeProvider _provider = new();
    private TestLibrary _library = null!;
    private CoreServices _services = null!;
    private FakeStorage _storage = null!;

    public TestContext TestContext { get; set; } = null!;

    private CancellationToken Token => TestContext.CancellationToken;

    private ProviderCapabilities Capabilities => _provider.Capabilities;

    [TestInitialize]
    public async Task InitializeAsync()
    {
        _library = new TestLibrary();
        _services = await CoreServices.CreateAsync(_library);
        var account = _provider
            .Account("acct")
            .WithFile("Photos/Trips/Beach.jpg", "beach")
            .WithFile("Photos/Trips/Day 1/Hill.jpg", "hill")
            .WithFile("Docs/Notes.md", "notes");
        _storage = new FakeStorage(account, "", Capabilities);
        var target = new ConnectionIndexTarget(Id, "", null, Capabilities, false);
        await _services.Get<ConnectionIndexer>().SyncAsync(target, new RecordingIndexSource(_storage), new(), Token);
    }

    [TestCleanup]
    public async Task CleanupAsync()
    {
        await _services.DisposeAsync();
        SqliteConnection.ClearAllPools();
        _library.Dispose();
    }

    [TestMethod]
    public async Task AFolderMadeAndAFileUploadedAreIndexedAtOnce()
    {
        var folder = await _storage.CreateFolderAsync("Docs/New", Token);
        await ApplyAsync(ConnectionItemChange.Put(Id, Capabilities, folder));
        var file = await UploadAsync("Docs/New/Report.PDF");
        await ApplyAsync(ConnectionItemChange.Put(Id, Capabilities, file));

        await using var db = Db();
        var made = await db.RemoteDirectories.SingleAsync(d => d.FullName == "docs/new", Token);
        Assert.IsTrue(made.Listed);
        var row = await db.RemoteFiles.SingleAsync(f => f.FullName == "docs/new/report.pdf", Token);
        Assert.AreEqual((made.Id, "Report", ".PDF"), (row.DirectoryId, row.Name, row.Extension));
    }

    [TestMethod]
    public async Task AFolderMovedTakesItsRowsAlongAndFilesKeepTheirTags()
    {
        var hill = await TagAsync("photos/trips/day 1/hill.jpg");

        var moved = await _storage.MoveAsync("Photos/Trips", "Docs/Trips", RemoteConflict.Fail, Token);
        await ApplyAsync(ConnectionItemChange.Moved(Id, Capabilities, "Photos/Trips", moved));

        await using var db = Db();
        var row = await db.RemoteFiles.Include(f => f.TagLinks).SingleAsync(f => f.Id == hill, Token);
        Assert.AreEqual(("docs/trips/day 1/hill.jpg", "Docs/Trips/Day 1/Hill.jpg"), (row.FullName, row.DisplayPath));
        Assert.HasCount(1, row.TagLinks);
        var trips = await db.RemoteDirectories.SingleAsync(d => d.FullName == "docs/trips", Token);
        var docs = await db.RemoteDirectories.SingleAsync(d => d.FullName == "docs", Token);
        Assert.AreEqual(docs.Id, trips.ParentId);
        Assert.IsFalse(await db.RemoteDirectories.AnyAsync(d => d.FullName.StartsWith("photos/trips"), Token));
    }

    [TestMethod]
    public async Task AFileRenamedKeepsItsRowAndADeletedOneIsMarkedThenDroppedBySync()
    {
        var notes = await TagAsync("docs/notes.md");
        var renamed = await _storage.MoveAsync("Docs/Notes.md", "Docs/Old notes.md", RemoteConflict.Fail, Token);
        await ApplyAsync(ConnectionItemChange.Moved(Id, Capabilities, "Docs/Notes.md", renamed));
        await _storage.DeleteAsync("Photos/Trips", Token);
        await ApplyAsync(ConnectionItemChange.Removed(Id, Capabilities, "Photos/Trips"));

        await using (var db = Db())
        {
            Assert.AreEqual(
                "docs/old notes.md",
                (await db.RemoteFiles.SingleAsync(f => f.Id == notes, Token)).FullName
            );
            Assert.IsTrue(
                await db.RemoteFiles.Where(f => f.FullName.StartsWith("photos/")).AllAsync(f => f.Removed, Token)
            );
        }

        var target = new ConnectionIndexTarget(Id, "", null, Capabilities, false);
        var result = await _services
            .Get<ConnectionIndexer>()
            .SyncAsync(target, new RecordingIndexSource(_storage), new(), Token);

        Assert.AreEqual(1L, result.Files);
        await using var after = Db();
        Assert.AreEqual(notes, (await after.RemoteFiles.SingleAsync(Token)).Id);
        Assert.AreEqual(1, await after.RemoteFileTags.CountAsync(Token));
    }

    [TestMethod]
    public async Task AFolderCopiedIsStillToListAndAConnectionNotIndexedHereIsLeftAlone()
    {
        var copy = await _storage.CopyAsync("Photos/Trips", "Docs/Trips copy", RemoteConflict.Fail, Token);
        await ApplyAsync(ConnectionItemChange.Copied(Id, Capabilities, copy));
        await ApplyAsync(ConnectionItemChange.Put(Id + 1, Capabilities, copy));

        await using var db = Db();
        var copied = await db.RemoteDirectories.SingleAsync(d => d.FullName == "docs/trips copy", Token);
        Assert.IsFalse(copied.Listed);
        Assert.IsFalse(await db.RemoteDirectories.AnyAsync(d => d.ConnectionId == Id + 1, Token));
    }

    [TestMethod]
    public async Task ClearingAConnectionDropsAllItsRowsAndTagsInBatches()
    {
        await using (var db = Db())
        {
            var root = await db.RemoteDirectories.SingleAsync(d => d.FullName == "", Token);
            for (var i = 0; i < RemoteIndexCleanup.BatchSize * 2 + 10; i++)
            {
                var file = new RemoteFile
                {
                    ConnectionId = Id,
                    DirectoryId = root.Id,
                    FullName = $"bulk {i}.txt",
                    DisplayPath = $"Bulk {i}.txt",
                    Name = $"Bulk {i}",
                    Extension = ".txt",
                };
                file.TagLinks.Add(new RemoteFileTag { TagId = BuiltInTags.Favorite });
                db.RemoteFiles.Add(file);
            }
            await db.SaveChangesAsync(Token);
        }

        await _services.Get<RemoteIndexCleanup>().ClearAsync(Id, Token);

        await using var after = Db();
        Assert.AreEqual(0, await after.RemoteFiles.CountAsync(Token));
        Assert.AreEqual(0, await after.RemoteDirectories.CountAsync(Token));
        Assert.AreEqual(0, await after.RemoteFileTags.CountAsync(Token));
        Assert.AreEqual(0, await after.RemoteIndexes.CountAsync(Token));
    }

    private Task ApplyAsync(ConnectionItemChange change) =>
        _services.Get<ConnectionIndexUpdates>().ApplyAsync(change, Token);

    private async Task<RemoteItem> UploadAsync(string path)
    {
        var upload = await _storage.BeginUploadAsync(Token);
        await _storage.AppendUploadAsync(upload, 0, new MemoryStream([1, 2, 3]), Token);
        return await _storage.FinishUploadAsync(upload, new RemoteUploadTarget(path, 3, RemoteConflict.Fail), Token);
    }

    private SfeDbContext Db() => _services.Get<IDbContextFactory<SfeDbContext>>().CreateDbContext();

    private async Task<long> TagAsync(string key)
    {
        await using var db = Db();
        var row = await db.RemoteFiles.SingleAsync(f => f.FullName == key, Token);
        db.RemoteFileTags.Add(new RemoteFileTag { FileId = row.Id, TagId = BuiltInTags.Favorite });
        await db.SaveChangesAsync(Token);
        return row.Id;
    }
}
