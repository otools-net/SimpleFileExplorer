using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Otools.Net.Sfe.Core.Connections;
using Otools.Net.Sfe.Core.Connections.Indexing;
using Otools.Net.Sfe.Core.Data;
using Otools.Net.Sfe.Core.Tagging;
using Otools.Net.Sfe.Tests.Infrastructure;

namespace Otools.Net.Sfe.Tests;

/// <summary>
/// The index of a connection's files in a library (<see cref="ConnectionIndexer"/>) against the fake provider: the
/// first listing in pages, then the changes by cursor; renames keeping rows and tags; a cursor reset listing again by
/// ids; a restart in the middle of a listing; paths too long; a provider without a cursor; another root folder.
/// </summary>
[TestClass]
public sealed class ConnectionIndexerTests
{
    private const int Id = 7;

    private readonly FakeProvider _provider = new();
    private TestLibrary _library = null!;
    private CoreServices _services = null!;
    private FakeAccount _account = null!;
    private FakeStorage _storage = null!;

    public TestContext TestContext { get; set; } = null!;

    private CancellationToken Token => TestContext.CancellationToken;

    [TestInitialize]
    public async Task InitializeAsync()
    {
        _library = new TestLibrary();
        _services = await CoreServices.CreateAsync(_library);
        _account = _provider
            .Account("acct")
            .WithFile("Photos/Trips/Beach.JPG", "beach")
            .WithFile("Photos/Trips/Hill.jpg", "hill")
            .WithFile("Photos/a.txt", "a")
            .WithFile("Docs/Notes.md", "notes")
            .WithFile("Docs/Big/1.txt", "1")
            .WithFile("Docs/Big/2.txt", "2")
            .WithFile("Docs/Big/3.txt", "3")
            .WithFile("Docs/Big/4.txt", "4")
            .WithFile("Docs/Big/5.txt", "5")
            .WithFile("Readme.txt", "read me");
        _storage = new FakeStorage(_account, "", _provider.Capabilities);
    }

    [TestCleanup]
    public async Task CleanupAsync()
    {
        await _services.DisposeAsync();
        SqliteConnection.ClearAllPools();
        _library.Dispose();
    }

    [TestMethod]
    public async Task TheFirstListingIndexesEverythingThenTheChangesComeByCursor()
    {
        var first = await SyncAsync();

        Assert.IsTrue(first.Listed);
        Assert.AreEqual((10L, 5L), (first.Files, first.Folders), "The root counts as a folder.");
        var beach = await FileAsync("photos/trips/beach.jpg");
        Assert.AreEqual(("Photos/Trips/Beach.JPG", "Beach", ".JPG"), (beach.DisplayPath, beach.Name, beach.Extension));
        Assert.AreEqual(5, beach.Size);
        Assert.IsNotNull(beach.ProviderId);
        Assert.IsNotNull(beach.ContentHash);
        await using (var db = Db())
        {
            Assert.IsTrue(await db.RemoteDirectories.AllAsync(d => d.Listed && !d.Pending, Token));
            var state = await db.RemoteIndexes.SingleAsync(Token);
            Assert.AreEqual(RemoteIndexPhase.Following, state.Phase);
            Assert.IsNotNull(state.LastSync);
            Assert.IsNotNull(state.ChangeCursor);
        }

        _account.WithFile("Photos/New.png", "new");
        await _storage.DeleteAsync("Docs/Big", Token);
        var source = new RecordingIndexSource(_storage);
        var second = await SyncAsync(source);

        Assert.IsFalse(second.Listed);
        Assert.IsTrue(source.Calls.All(c => c.StartsWith("changes", StringComparison.Ordinal)));
        Assert.AreEqual((6L, 4L), (second.Files, second.Folders));
        Assert.IsNotNull(await FindAsync("photos/new.png"));
        Assert.IsNull(await FindAsync("docs/big/1.txt"));
    }

    [TestMethod]
    public async Task FilesRenamedOrMovedAtTheProviderKeepTheirRowsAndTags()
    {
        await SyncAsync();
        var hill = await TagAsync("photos/trips/hill.jpg");
        var notes = await TagAsync("docs/notes.md");

        await _storage.MoveAsync("Photos/Trips", "Docs/Trips 2026", RemoteConflict.Fail, Token);
        await _storage.MoveAsync("Docs/Notes.md", "Docs/Notes (old).md", RemoteConflict.Fail, Token);
        await SyncAsync();

        var moved = await FileAsync("docs/trips 2026/hill.jpg");
        var renamed = await FileAsync("docs/notes (old).md");
        Assert.AreEqual((hill, notes), (moved.Id, renamed.Id));
        Assert.AreEqual("Docs/Trips 2026/Hill.jpg", moved.DisplayPath);
        Assert.AreEqual(2, await TagCountAsync());
        Assert.IsNull(await FindAsync("photos/trips/hill.jpg"));
        await using var db = Db();
        Assert.IsFalse(await db.RemoteDirectories.AnyAsync(d => d.FullName == "photos/trips", Token));
        var folder = await db.RemoteDirectories.SingleAsync(d => d.FullName == "docs/trips 2026", Token);
        Assert.AreEqual(folder.Id, moved.DirectoryId);
    }

    [TestMethod]
    public async Task ACursorResetListsEverythingAgainAndFilesKeepTheirTagsByTheirIds()
    {
        await SyncAsync();
        var hill = await TagAsync("photos/trips/hill.jpg");
        // While the cursor is lost: a rename, a delete, a new file.
        await _storage.MoveAsync("Photos/Trips/Hill.jpg", "Photos/Hill.jpg", RemoteConflict.Fail, Token);
        await _storage.DeleteAsync("Readme.txt", Token);
        _account.WithFile("Docs/Later.txt", "later");
        _account.ResetCursors = true;

        var source = new RecordingIndexSource(_storage);
        var result = await SyncAsync(source);

        Assert.IsTrue(result.Listed);
        Assert.IsTrue(source.Calls.Any(c => c.StartsWith("list ", StringComparison.Ordinal)));
        Assert.AreEqual(hill, (await FileAsync("photos/hill.jpg")).Id);
        Assert.AreEqual(1, await TagCountAsync());
        Assert.IsNull(await FindAsync("readme.txt"));
        Assert.IsNotNull(await FindAsync("docs/later.txt"));
        Assert.AreEqual(10L, result.Files);
        await using var db = Db();
        Assert.AreEqual(2, (await db.RemoteIndexes.SingleAsync(Token)).Pass);
        Assert.IsFalse(await db.RemoteFiles.AnyAsync(f => f.Removed, Token));
    }

    [TestMethod]
    public async Task AnUploadIntoAFolderAPassHasNotReachedKeepsTheFolderToListAndItsFilesTags()
    {
        await SyncAsync();
        var tagged = await TagAsync("docs/big/1.txt");
        _account.ResetCursors = true;
        // The cursor reset lists everything again; the server stops after the root's first page.
        var stopped = new RecordingIndexSource(_storage) { FailAfter = 1 };
        await Assert.ThrowsExactlyAsync<OperationCanceledException>(() => SyncAsync(stopped));
        _account.ResetCursors = false;

        var uploaded = await PutAsync("Docs/Big/6.txt");
        await _services
            .Get<ConnectionIndexUpdates>()
            .ApplyAsync(ConnectionItemChange.Put(Id, _provider.Capabilities, uploaded), Token);
        var result = await SyncAsync();

        Assert.AreEqual((11L, 5L), (result.Files, result.Folders));
        foreach (var key in new[] { "docs/notes.md", "docs/big/1.txt", "docs/big/5.txt", "docs/big/6.txt" })
            Assert.IsNotNull(await FindAsync(key), key);
        Assert.AreEqual(tagged, (await FileAsync("docs/big/1.txt")).Id);
        Assert.AreEqual(1, await TagCountAsync());
        await using var db = Db();
        Assert.IsTrue(await db.RemoteDirectories.AllAsync(d => d.Listed && !d.Pending && !d.Removed, Token));
    }

    [TestMethod]
    public async Task AListingStoppedByARestartGoesOnFromTheLastPageWritten()
    {
        // Pages of 2: the root, Docs, Photos, then Docs/Big's first page… the 5th page throws.
        var stopped = new RecordingIndexSource(_storage) { FailAfter = 5 };
        await Assert.ThrowsExactlyAsync<OperationCanceledException>(() => SyncAsync(stopped));
        await using (var db = Db())
        {
            var state = await db.RemoteIndexes.SingleAsync(Token);
            Assert.AreEqual(RemoteIndexPhase.Listing, state.Phase);
            Assert.IsNull(state.LastSync);
        }

        var resumed = new RecordingIndexSource(_storage);
        var result = await SyncAsync(resumed);

        Assert.AreEqual((10L, 5L), (result.Files, result.Folders));
        var pages = stopped.Calls.Concat(resumed.Calls).Count(c => c.StartsWith("list ", StringComparison.Ordinal));
        Assert.AreEqual(Pages(), pages, string.Join("\n", stopped.Calls.Concat(resumed.Calls)));
        Assert.DoesNotContain("list  @", resumed.Calls, "The root was not listed again.");
        Assert.IsTrue(resumed.Calls.Any(c => c.StartsWith("changes @", StringComparison.Ordinal) && c.Length > 9));
    }

    [TestMethod]
    public async Task PathsLongerThanTheIndexStoresAreLeftOutAndCounted()
    {
        var name = new string('x', 255) + ".txt";
        _account.WithFile($"Docs/{name}", "long");

        var result = await SyncAsync();

        Assert.AreEqual(1, result.TooLong);
        Assert.AreEqual(10L, result.Files);
        await using var db = Db();
        Assert.AreEqual(1, (await db.RemoteIndexes.SingleAsync(Token)).TooLong);
        Assert.IsTrue(await db.RemoteFiles.AllAsync(f => f.FullName.Length <= SfeDbContext.MaxPathLength, Token));
    }

    [TestMethod]
    public async Task AProviderWithoutACursorIsListedAgainAtEachSync()
    {
        var capabilities = _provider.Capabilities with { ChangeCursor = false };
        _storage = new FakeStorage(_account, "", capabilities);
        await SyncAsync(capabilities: capabilities);
        var hill = await TagAsync("photos/trips/hill.jpg");
        await _storage.DeleteAsync("Docs", Token);

        var source = new RecordingIndexSource(_storage);
        var result = await SyncAsync(source, capabilities);

        Assert.IsFalse(source.Calls.Any(c => c.StartsWith("changes", StringComparison.Ordinal)));
        Assert.IsTrue(result.Listed);
        Assert.AreEqual((4L, 3L), (result.Files, result.Folders));
        Assert.AreEqual(hill, (await FileAsync("photos/trips/hill.jpg")).Id);
    }

    [TestMethod]
    public async Task AnotherRootFolderOrAccountDropsTheRowsFirst()
    {
        await SyncAsync();
        await TagAsync("photos/trips/hill.jpg");

        var photos = new FakeStorage(_account, "Photos", _provider.Capabilities);
        var target = new ConnectionIndexTarget(Id, "Photos", "account", _provider.Capabilities, false);
        var result = await Indexer.SyncAsync(target, new RecordingIndexSource(photos), new(), Token);

        Assert.AreEqual((3L, 2L), (result.Files, result.Folders));
        Assert.AreEqual("Trips/Hill.jpg", (await FileAsync("trips/hill.jpg")).DisplayPath);
        Assert.AreEqual(0, await TagCountAsync());
    }

    [TestMethod]
    public async Task OtherConnectionsRowsAreNeverTouched()
    {
        await SyncAsync();
        var other = new FakeStorage(
            _provider.Account("other").WithFile("Photos/x.txt", "x"),
            "",
            _provider.Capabilities
        );
        var target = new ConnectionIndexTarget(Id + 1, "", "other", _provider.Capabilities, false);
        await Indexer.SyncAsync(target, new RecordingIndexSource(other), new(), Token);
        await _services.Get<RemoteIndexCleanup>().ClearAsync(Id + 1, Token);

        await using var db = Db();
        Assert.AreEqual(10, await db.RemoteFiles.CountAsync(Token));
        Assert.IsTrue(await db.RemoteFiles.AllAsync(f => f.ConnectionId == Id, Token));
    }

    [TestMethod]
    public async Task ADeletedFolderTakesOnlyWhatIsBelowIt()
    {
        _account.WithFile("A_b/x.txt", "x").WithFile("Axb/y.txt", "y").WithFile("A_b0/z.txt", "z");
        await SyncAsync();

        await _storage.DeleteAsync("A_b", Token);
        await SyncAsync();

        Assert.IsNull(await FindAsync("a_b/x.txt"));
        Assert.IsNotNull(await FindAsync("axb/y.txt"));
        Assert.IsNotNull(await FindAsync("a_b0/z.txt"));
    }

    /// <summary>The listing pages of the whole tree at the fake's 2 items a page.</summary>
    private int Pages()
    {
        var folders = new[] { "", "Photos", "Photos/Trips", "Docs", "Docs/Big" };
        return folders.Sum(f => Math.Max(1, (int)Math.Ceiling(CountAll(f) / 2.0)));
    }

    private int CountAll(string folder)
    {
        var count = 0;
        string? cursor = null;
        do
        {
            var page = _storage.ListAsync(folder, cursor, Token).Result;
            count += page.Items.Count;
            cursor = page.Cursor;
        } while (cursor is not null);
        return count;
    }

    private ConnectionIndexer Indexer => _services.Get<ConnectionIndexer>();

    private Task<ConnectionIndexResult> SyncAsync(
        RecordingIndexSource? source = null,
        ProviderCapabilities? capabilities = null
    )
    {
        var target = new ConnectionIndexTarget(Id, "", "account", capabilities ?? _provider.Capabilities, false);
        return Indexer.SyncAsync(target, source ?? new RecordingIndexSource(_storage), new(), Token);
    }

    private SfeDbContext Db() => _services.Get<IDbContextFactory<SfeDbContext>>().CreateDbContext();

    private async Task<RemoteFile?> FindAsync(string key)
    {
        await using var db = Db();
        return await db.RemoteFiles.AsNoTracking().FirstOrDefaultAsync(f => f.FullName == key && !f.Removed, Token);
    }

    private async Task<RemoteFile> FileAsync(string key) =>
        await FindAsync(key) ?? throw new AssertFailedException($"No row for {key}.");

    /// <summary>Tags a file's row (as phase 4's dialogs will); its id.</summary>
    private async Task<long> TagAsync(string key)
    {
        var row = await FileAsync(key);
        await using var db = Db();
        db.RemoteFileTags.Add(new RemoteFileTag { FileId = row.Id, TagId = BuiltInTags.Favorite });
        await db.SaveChangesAsync(Token);
        return row.Id;
    }

    /// <summary>A file made at the provider through SFE (an upload); the item as the provider answers it.</summary>
    private async Task<RemoteItem> PutAsync(string path)
    {
        _account.WithFile(path, "uploaded through SFE");
        return await _storage.GetAsync(path, Token) ?? throw new AssertFailedException($"No {path}.");
    }

    private async Task<int> TagCountAsync()
    {
        await using var db = Db();
        return await db.RemoteFileTags.CountAsync(Token);
    }
}
