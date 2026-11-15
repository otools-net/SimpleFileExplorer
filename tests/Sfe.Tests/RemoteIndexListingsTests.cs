using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Otools.Net.Sfe.Core.Connections;
using Otools.Net.Sfe.Core.Connections.Indexing;
using Otools.Net.Sfe.Core.Data;
using Otools.Net.Sfe.Core.Library;
using Otools.Net.Sfe.Tests.Infrastructure;

namespace Otools.Net.Sfe.Tests;

/// <summary>
/// A connection's folders listed from a library's index (<see cref="RemoteIndexListings"/>): only when the index holds
/// the folder whole and is current; folders first and every sort of the listing, counted, paged by key from the last
/// row of the page before; rows marked removed and children folder rules hide in no page or count; which items of a
/// page read from the provider the index holds.
/// </summary>
[TestClass]
public class RemoteIndexListingsTests
{
    private const int Id = 7;
    private const int Files = 25;

    private static readonly string[] Extensions = [".jpg", ".png", ".txt"];

    private readonly FakeProvider _provider = new();
    private TestLibrary _library = null!;
    private CoreServices _services = null!;
    private FakeAccount _account = null!;
    private FakeStorage _storage = null!;

    public TestContext TestContext { get; set; } = null!;

    private CancellationToken Token => TestContext.CancellationToken;

    private ConnectionIndexTarget Target => new(Id, "", "account", _provider.Capabilities, false);

    [TestInitialize]
    public async Task InitializeAsync()
    {
        _library = new TestLibrary();
        _services = await CreateServicesAsync(_library);
        _account = _provider.Account("acct").WithFolder("Big/Sub a").WithFolder("Big/Sub B").WithFolder("Big/sub c");
        var start = new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc);
        for (var i = 0; i < Files; i++)
        {
            // Sizes and dates all differ, in an order of their own; extensions come back every third file.
            var content = new string('x', (i * 7 % Files) + 1);
            _account.WithFile($"Big/F{i:00}{Extensions[i % 3]}", content, start.AddHours(i * 11 % Files));
        }
        _account.WithFile("Readme.txt", "read me");
        _storage = new FakeStorage(_account, "", _provider.Capabilities);
    }

    [TestCleanup]
    public async Task CleanupAsync()
    {
        if (_services is not null)
        {
            await DropAsync(_services);
            await _services.DisposeAsync();
        }
        SqliteConnection.ClearAllPools();
        _library.Dispose();
    }

    /// <summary>The services on SQLite (<see cref="RemoteIndexListingsSqlServerTests"/>: on SQL Server).</summary>
    protected virtual Task<CoreServices> CreateServicesAsync(TestLibrary library) => CoreServices.CreateAsync(library);

    /// <summary>Drops what the services' database left outside the test's folder.</summary>
    protected virtual Task DropAsync(CoreServices services) => Task.CompletedTask;

    [TestMethod]
    public async Task EverySortListsFoldersFirstThenFilesByItsValueAndPagesAddUpToTheFolder()
    {
        await SyncAsync();
        var items = (await ListAllAsync("Big")).ToList();

        foreach (var sort in new[] { SortField.Name, SortField.Modified, SortField.Size, SortField.Type })
        {
            foreach (var descending in new[] { false, true })
            {
                var (pages, totals) = await PagesAsync(new RemoteListingQuery(Id, "Big", 0, 4, sort, descending));
                var what = $"{sort} {(descending ? "down" : "up")}";
                Assert.IsTrue(totals.All(t => t == Files + 3), what);
                CollectionAssert.AreEquivalent(items.Select(i => i.Path).ToList(), pages.Select(i => i.Path).ToList());
                CollectionAssert.AreEqual(Expected(items, sort, descending), pages.Select(i => i.Name).ToList(), what);
            }
        }
    }

    [TestMethod]
    public async Task ANextPageGoesOnFromTheLastRowOfThePageBeforeNotFromItsOffset()
    {
        await SyncAsync();
        var query = new RemoteListingQuery(Id, "Big", 0, 5, SortField.Name);
        var first = await ListAsync(query);
        await MarkRemovedAsync("big/f00.jpg");

        var next = await ListAsync(query with { Offset = 5 });
        var jump = await ListAsync(query with { Offset = 6, Limit = 4 });

        CollectionAssert.AreEqual(new[] { "Sub a", "Sub B", "sub c", "F00.jpg", "F01.png" }, Names(first));
        CollectionAssert.AreEqual(new[] { "F02.txt", "F03.jpg", "F04.png", "F05.txt", "F06.jpg" }, Names(next));
        Assert.AreEqual(Files + 3, next!.Total, "the counts read with the first page");
        CollectionAssert.AreEqual(new[] { "F04.png", "F05.txt", "F06.jpg", "F07.png" }, Names(jump), "by offset");
        Assert.AreEqual(Files + 2, jump!.Total);
    }

    [TestMethod]
    public async Task NothingIsListedFromTheIndexUnlessItHoldsTheFolderWholeAndIsCurrent()
    {
        var big = Asked(new RemoteListingQuery(Id, "Big", 0, 10));
        Assert.IsNull(await Listings.ListAsync(big, Token), "not indexed");
        await SyncAsync();
        Assert.IsNotNull(await Listings.ListAsync(big, Token));

        Assert.IsNull(await Listings.ListAsync(big with { Query = big.Query with { Path = "Nowhere" } }, Token));
        Assert.IsNull(await Listings.ListAsync(big with { Target = Target with { RootPath = "Big" } }, Token));
        Assert.IsNull(await Listings.ListAsync(big with { Target = Target with { AccountKey = "other" } }, Token));
        Assert.IsNull(
            await Listings.ListAsync(big with { Query = big.Query with { Offset = 20 }, ProviderPaging = true }, Token),
            "the provider's read goes on"
        );
        await UpdateFolderAsync("big", s => s.SetProperty(d => d.Pending, true));
        Assert.IsNull(await Listings.ListAsync(big, Token), "the folder is still to list");
        await UpdateFolderAsync("big", s => s.SetProperty(d => d.Pending, false).SetProperty(d => d.Listed, false));
        Assert.IsNull(await Listings.ListAsync(big, Token), "the folder was never listed");
        await UpdateFolderAsync("big", s => s.SetProperty(d => d.Listed, true));
        await UpdateStateAsync(s => s.SetProperty(i => i.Phase, RemoteIndexPhase.Listing));
        Assert.IsNull(await Listings.ListAsync(big, Token), "a full listing goes on");
        var old = DateTime.UtcNow - RemoteIndexListings.MaxAge - TimeSpan.FromMinutes(1);
        await UpdateStateAsync(s =>
            s.SetProperty(i => i.Phase, RemoteIndexPhase.Following).SetProperty(i => i.LastSync, old)
        );
        Assert.IsNull(await Listings.ListAsync(big, Token), "synced too long ago");
    }

    [TestMethod]
    public async Task RemovedRowsAndChildrenTheRulesHideAreInNoPageOrCount()
    {
        await SyncAsync();
        await MarkRemovedAsync("big/f01.png");
        string hiddenId;
        await using (var db = Db())
            hiddenId = (await db.RemoteFiles.SingleAsync(f => f.FullName == "big/f02.txt", Token)).ProviderId!;
        var hidden = new ListingFilter(new HashSet<string>(["SUB A", "F03.jpg"], StringComparer.OrdinalIgnoreCase));
        var asked = Asked(new RemoteListingQuery(Id, "Big", 0, 3)) with
        {
            Filter = hidden,
            HiddenIds = new HashSet<string>([hiddenId]),
        };

        var (pages, totals) = await PagesAsync(asked.Query, asked);
        var only = await Listings.ListAsync(
            asked with
            {
                Filter = new ListingFilter(new HashSet<string>(), new HashSet<string>(["sub c", "nothing"])),
            },
            Token
        );

        var names = pages.Select(i => i.Name).ToList();
        CollectionAssert.IsSubsetOf(new[] { "Sub B", "sub c", "F00.jpg", "F04.png" }, names);
        CollectionAssert.DoesNotContain(names, "Sub a");
        CollectionAssert.DoesNotContain(names, "F01.png");
        CollectionAssert.DoesNotContain(names, "F02.txt");
        CollectionAssert.DoesNotContain(names, "F03.jpg");
        Assert.HasCount(Files + 3 - 4, names);
        Assert.IsTrue(totals.All(t => t == Files + 3 - 4));
        CollectionAssert.AreEqual(new[] { "sub c" }, Names(only));
        Assert.AreEqual((1, 1), (only!.Total, only.DirectoryCount));
    }

    [TestMethod]
    public async Task NamesComeInTheOrderOfTheLibrarysListingsNumbersCaseAndAccentsAlike()
    {
        string[] files =
        [
            "file10.txt",
            "file2.txt",
            "File1.txt",
            "file02b.txt",
            "éclair.txt",
            "eclair.txt",
            "Eclair2.txt",
            "Zebra.txt",
            "a b.txt",
            "a-b.txt",
            "ab9.txt",
            "Ab10.txt",
            "résumé.txt",
            "resume.txt",
            "_x.txt",
            "9.txt",
        ];
        foreach (var name in files)
            _account.WithFile($"Mixed/{name}", "same size", new DateTime(2026, 2, 1, 0, 0, 0, DateTimeKind.Utc));
        foreach (var name in new[] { "Season 10", "season 2", "Été", "ete" })
            _account.WithFolder($"Mixed/{name}");
        await SyncAsync();
        var provider = await ListAllAsync("Mixed");

        foreach (
            var (sort, descending) in new[] { (SortField.Name, false), (SortField.Name, true), (SortField.Size, false) }
        )
        {
            var (pages, _) = await PagesAsync(new RemoteListingQuery(Id, "Mixed", 0, 3, sort, descending));
            var library = RemoteSort.Sort(provider, sort, descending).Select(i => i.Name).ToList();
            CollectionAssert.AreEqual(library, pages.Select(i => i.Name).ToList(), $"{sort} {descending}");
        }
    }

    [TestMethod]
    public async Task RowsIndexedBeforeTheirNameKeyAreListedFromTheProviderUntilASyncFillsThem()
    {
        await SyncAsync();
        await using (var db = Db())
        {
            await db.RemoteFiles.ExecuteUpdateAsync(s => s.SetProperty(f => f.NameKey, (string?)null), Token);
            await db.RemoteDirectories.ExecuteUpdateAsync(s => s.SetProperty(d => d.NameKey, (string?)null), Token);
        }
        var big = Asked(new RemoteListingQuery(Id, "Big", 0, 50));
        Assert.IsNull(await Listings.ListAsync(big, Token));

        await SyncAsync();

        var listed = await Listings.ListAsync(big, Token);
        Assert.AreEqual(Files + 3, listed!.Total);
        await using var after = Db();
        Assert.IsFalse(await after.RemoteFiles.AnyAsync(f => f.NameKey == null, Token));
        Assert.IsFalse(await after.RemoteDirectories.AnyAsync(d => d.NameKey == null, Token));
    }

    [TestMethod]
    public async Task APageReadFromTheProviderIsToldWhatTheIndexHolds()
    {
        Assert.IsNull(await Listings.IndexedAsync(Target, [], Token), "no index of the connection here");
        await SyncAsync();
        _account.WithFile("Big/New.txt", "after the sync").WithFolder("Big/Sub d");
        var read = await ListAllAsync("Big");

        var indexed = await Listings.IndexedAsync(Target, read, Token);

        Assert.IsNotNull(indexed);
        Assert.IsTrue(indexed.Contains("Big/F00.jpg") && indexed.Contains("Big/Sub a"));
        Assert.IsFalse(indexed.Contains("Big/New.txt") || indexed.Contains("Big/Sub d"));
        Assert.HasCount(Files + 3, indexed);
    }

    private RemoteIndexListings Listings => _services.Get<RemoteIndexListings>();

    private IndexListingQuery Asked(RemoteListingQuery query) =>
        new(query, Target, null, new HashSet<string>(), ProviderPaging: false);

    private async Task<RemoteListing?> ListAsync(RemoteListingQuery query) =>
        await Listings.ListAsync(Asked(query), Token);

    /// <summary>Every page of the folder, one after the other as the client scrolls, and their totals.</summary>
    private async Task<(List<RemoteItem> Items, List<int> Totals)> PagesAsync(
        RemoteListingQuery query,
        IndexListingQuery? asked = null
    )
    {
        var (items, totals) = (new List<RemoteItem>(), new List<int>());
        for (var offset = 0; offset < 100; offset += query.Limit)
        {
            var at = asked ?? Asked(query);
            var page = await Listings.ListAsync(at with { Query = at.Query with { Offset = offset } }, Token);
            Assert.IsNotNull(page);
            items.AddRange(page.Items);
            totals.Add(page.Total);
            if (offset + page.Items.Count >= page.Total)
                break;
        }
        return (items, totals);
    }

    /// <summary>The order the index lists in: folders first, the sort's value then the lowered path, one way.</summary>
    private static List<string> Expected(List<RemoteItem> items, SortField sort, bool descending)
    {
        var folders = items.Where(i => i.IsFolder).OrderBy(i => i.Path.ToLowerInvariant(), StringComparer.Ordinal);
        var files = items.Where(i => !i.IsFolder).ToList();
        Func<RemoteItem, IComparable> value = sort switch
        {
            SortField.Modified => i => i.Modified!.Value,
            SortField.Size => i => i.Size!.Value,
            SortField.Type => i => Path.GetExtension(i.Name).ToLowerInvariant(),
            _ => i => i.Path.ToLowerInvariant(),
        };
        var ordered = descending
            ? files.OrderByDescending(value).ThenByDescending(i => i.Path.ToLowerInvariant(), StringComparer.Ordinal)
            : files.OrderBy(value).ThenBy(i => i.Path.ToLowerInvariant(), StringComparer.Ordinal);
        var shownFolders = descending && sort is SortField.Name or SortField.Modified ? folders.Reverse() : folders;
        return [.. shownFolders.Concat(ordered).Select(i => i.Name)];
    }

    private static string[] Names(RemoteListing? listing) => [.. listing!.Items.Select(i => i.Name)];

    /// <summary>The folder as the provider lists it, every page.</summary>
    private async Task<List<RemoteItem>> ListAllAsync(string path)
    {
        var items = new List<RemoteItem>();
        string? cursor = null;
        do
        {
            var page = await _storage.ListAsync(path, cursor, Token);
            items.AddRange(page.Items);
            cursor = page.Cursor;
        } while (cursor is not null);
        return items;
    }

    private Task SyncAsync() =>
        _services.Get<ConnectionIndexer>().SyncAsync(Target, new RecordingIndexSource(_storage), new(), Token);

    private SfeDbContext Db() => _services.Get<IDbContextFactory<SfeDbContext>>().CreateDbContext();

    private async Task MarkRemovedAsync(string key)
    {
        await using var db = Db();
        await db
            .RemoteFiles.Where(f => f.FullName == key)
            .ExecuteUpdateAsync(s => s.SetProperty(f => f.Removed, true), Token);
    }

    private async Task UpdateFolderAsync(
        string key,
        Action<Microsoft.EntityFrameworkCore.Query.UpdateSettersBuilder<RemoteDirectory>> set
    )
    {
        await using var db = Db();
        await db.RemoteDirectories.Where(d => d.FullName == key).ExecuteUpdateAsync(set, Token);
    }

    private async Task UpdateStateAsync(
        Action<Microsoft.EntityFrameworkCore.Query.UpdateSettersBuilder<RemoteIndexState>> set
    )
    {
        await using var db = Db();
        await db.RemoteIndexes.ExecuteUpdateAsync(set, Token);
    }
}
