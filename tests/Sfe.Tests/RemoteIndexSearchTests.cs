using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Otools.Net.Sfe.Core.Access;
using Otools.Net.Sfe.Core.Connections;
using Otools.Net.Sfe.Core.Connections.Indexing;
using Otools.Net.Sfe.Core.Data;
using Otools.Net.Sfe.Core.Library;
using Otools.Net.Sfe.Core.Tagging;
using Otools.Net.Sfe.Tests.Infrastructure;

namespace Otools.Net.Sfe.Tests;

/// <summary>
/// A connection's files searched in a library's index (<see cref="RemoteIndexSearch"/>): the library's terms, every
/// sort paged by key with the match count, the folder searched, rows marked removed and of other connections never
/// found, folder rules as conditions (counts too), names by their natural key with rows lacking one after the others,
/// and how much of the connection the index holds.
/// </summary>
[TestClass]
public class RemoteIndexSearchTests
{
    private const int Id = 7;
    private const int Files = 12;

    private static readonly string[] Extensions = [".jpg", ".mp4", ".txt"];

    private readonly FakeProvider _provider = new();
    private TestLibrary _library = null!;
    private CoreServices _services = null!;
    private FakeStorage _storage = null!;

    public TestContext TestContext { get; set; } = null!;

    private CancellationToken Token => TestContext.CancellationToken;

    private ConnectionIndexTarget Target => new(Id, "", "account", _provider.Capabilities, false);

    private RemoteIndexSearch Search => _services.Get<RemoteIndexSearch>();

    [TestInitialize]
    public async Task InitializeAsync()
    {
        _library = new TestLibrary();
        _services = await CreateServicesAsync(_library);
        var account = _provider.Account("acct");
        var start = new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc);
        for (var i = 0; i < Files; i++)
        {
            var content = new string('x', (i * 5 % Files) + 1);
            account.WithFile($"Big/Holiday {i + 1}{Extensions[i % 3]}", content, start.AddHours(i * 7 % Files));
        }
        account
            .WithFile("Big/Sub/Deep holiday.jpg", "deep")
            .WithFile("Bigger/Holiday elsewhere.jpg", "bigger")
            .WithFile("Readme.txt", "read me");
        _storage = new FakeStorage(account, "", _provider.Capabilities);
        await _services.Get<ConnectionIndexer>().SyncAsync(Target, new RecordingIndexSource(_storage), new(), Token);
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

    /// <summary>The services on SQLite (<see cref="RemoteIndexSearchSqlServerTests"/>: on SQL Server).</summary>
    protected virtual Task<CoreServices> CreateServicesAsync(TestLibrary library) => CoreServices.CreateAsync(library);

    protected virtual Task DropAsync(CoreServices services) => Task.CompletedTask;

    [TestMethod]
    [DataRow("holiday", 14)]
    [DataRow("HOLIDAY 1", 4)]
    [DataRow("e:jpg", 6)]
    [DataRow("-e:jpg", 9)]
    [DataRow("type:video", 4)]
    [DataRow("s:>5b", 9)]
    [DataRow("*big/sub", 1)]
    [DataRow("holiday, readme", 15)]
    [DataRow("w:>100", 0)]
    [DataRow("d:>1m", 0)]
    [DataRow("-d:>1m", 15)]
    public async Task TheLibrarysTermsFindTheConnectionsFiles(string query, int count)
    {
        var page = await SearchAsync(new RemoteSearchAsk { Target = Target, Query = query });

        Assert.AreEqual(count, page.Results.Total, query);
        Assert.HasCount(count, page.Results.Items, query);
        Assert.AreEqual(RemoteSearchCoverage.Complete, page.Coverage);
    }

    [TestMethod]
    public async Task ItemsAreAsTheListingsShowThemWithTheirSizeAndKind()
    {
        var page = await SearchAsync(new RemoteSearchAsk { Target = Target, Query = "deep" });

        var item = page.Results.Items.Single();
        Assert.AreEqual("Big/Sub/Deep holiday.jpg", item.Path, "the provider's spelling");
        Assert.AreEqual("Deep holiday.jpg", item.Name);
        Assert.AreEqual("Big/Sub", item.Directory);
        Assert.AreEqual(ItemKind.Image, item.Kind);
        Assert.AreEqual(4, item.Size);
        Assert.IsNotNull(page.ProviderIds.Single());
    }

    [TestMethod]
    public async Task DurationsAndResolutionsMatchOnceKnown()
    {
        await UpdateFilesAsync("big/holiday 2.mp4", s =>
            s.SetProperty(f => f.Width, 1920).SetProperty(f => f.Height, 1080).SetProperty(f => f.Duration, 90)
        );

        Assert.AreEqual(1, (await SearchAsync(Ask("w:1920 d:>1m"))).Results.Total);
        Assert.AreEqual(1, (await SearchAsync(Ask("r:1920x1080"))).Results.Total);
        var sorted = await SearchAsync(Ask("holiday") with { Sort = SortField.Duration, Descending = true });
        Assert.AreEqual("Holiday 2.mp4", sorted.Results.Items[0].Name, "known durations first");
        Assert.AreEqual(90, sorted.Results.Items[0].Duration);
    }

    [TestMethod]
    public async Task EverySortPagesThroughEveryMatchInItsOrder()
    {
        foreach (var sort in Enum.GetValues<SortField>())
        {
            foreach (var descending in new[] { false, true })
            {
                var all = await SearchAsync(Ask("holiday") with { Sort = sort, Descending = descending, Limit = 100 });
                var paged = await PagesAsync(Ask("holiday") with { Sort = sort, Descending = descending, Limit = 4 });

                var what = $"{sort} {(descending ? "down" : "up")}";
                CollectionAssert.AreEqual(all.Results.Items.Select(i => i.Path).ToList(), paged, what);
                Assert.HasCount(14, paged, what);
            }
        }
    }

    [TestMethod]
    public async Task NamesSortNaturallyAndRowsWithoutANameKeyComeAfterByPath()
    {
        var names = await SearchAsync(Ask("e:jpg") with { Sort = SortField.Name, Folder = "Big" });
        CollectionAssert.AreEqual(
            new[] { "Deep holiday.jpg", "Holiday 1.jpg", "Holiday 4.jpg", "Holiday 7.jpg", "Holiday 10.jpg" },
            names.Results.Items.Select(i => i.Name).ToList()
        );

        await UpdateFilesAsync("big/holiday 1.jpg", s => s.SetProperty(f => f.NameKey, (string?)null));
        foreach (var descending in new[] { false, true })
        {
            var ask = Ask("e:jpg") with { Sort = SortField.Name, Folder = "Big", Descending = descending, Limit = 2 };
            var paged = await PagesAsync(ask);
            Assert.AreEqual("Big/Holiday 1.jpg", paged[^1], "a row without its key yet comes last");
            Assert.HasCount(5, paged);
        }
    }

    [TestMethod]
    public async Task AFolderIsSearchedWithEverythingBelowItAndNothingBeside()
    {
        var big = await SearchAsync(Ask("holiday") with { Folder = "Big" });
        var sub = await SearchAsync(Ask("") with { Folder = "big/SUB" });

        Assert.AreEqual(13, big.Results.Total, "not Bigger/");
        Assert.IsFalse(big.Results.Items.Any(i => i.Path.StartsWith("Bigger/", StringComparison.Ordinal)));
        CollectionAssert.AreEqual(new[] { "Big/Sub/Deep holiday.jpg" }, sub.Results.Items.Select(i => i.Path).ToList());
    }

    [TestMethod]
    public async Task RemovedRowsAndOtherConnectionsAreNeverFound()
    {
        await UpdateFilesAsync("readme.txt", s => s.SetProperty(f => f.Removed, true));
        await UpdateFilesAsync("bigger/holiday elsewhere.jpg", s => s.SetProperty(f => f.ConnectionId, Id + 1));

        var page = await SearchAsync(Ask("readme, elsewhere"));

        Assert.AreEqual(0, page.Results.Total);
    }

    [TestMethod]
    public async Task FolderRulesAreConditionsOfTheQueryCountsIncluded()
    {
        var visible = new PathFilter([new PathRegion("", ["Big/Sub", "Bigger"])]);
        var ruled = await SearchAsync(Ask("holiday") with { Visible = visible });
        Assert.AreEqual(12, ruled.Results.Total);
        Assert.IsFalse(ruled.Results.Items.Any(i => i.Path.Contains("Sub/") || i.Path.StartsWith("Bigger")));

        var only = new PathFilter([new PathRegion("Big/Sub", [])]);
        Assert.AreEqual(1, (await SearchAsync(Ask("holiday") with { Visible = only })).Results.Total);
        Assert.AreEqual(0, (await SearchAsync(Ask("holiday") with { Visible = PathFilter.Nothing })).Results.Total);
    }

    [TestMethod]
    public async Task AFolderHiddenByItsIdHidesEverythingBelowItWhereverItIsNow()
    {
        string folderId;
        await using (var db = Db())
            folderId = (await db.RemoteDirectories.SingleAsync(d => d.FullName == "big/sub", Token)).ProviderId!;
        string fileId;
        await using (var db = Db())
            fileId = (await db.RemoteFiles.SingleAsync(f => f.FullName == "readme.txt", Token)).ProviderId!;

        var page = await SearchAsync(
            Ask("") with
            {
                HiddenIds = new HashSet<string> { folderId, fileId },
            }
        );

        Assert.AreEqual(Files + 1, page.Results.Total);
        Assert.IsFalse(page.Results.Items.Any(i => i.Path is "Big/Sub/Deep holiday.jpg" or "Readme.txt"));
    }

    [TestMethod]
    public async Task TagsAreFoundAndHiddenTagsLeftOutUnlessNamed()
    {
        var tags = _services.Get<TagService>();
        var cat = (await tags.CreateAsync(new TagInput("Cat"), Token)).Tag!.Id;
        await TagAsync("big/holiday 1.jpg", cat);
        await TagAsync("readme.txt", BuiltInTags.Archived);

        Assert.AreEqual(1, (await SearchAsync(Ask("t:cat"))).Results.Total);
        Assert.AreEqual("Cat", (await SearchAsync(Ask("t:cat"))).Results.Items[0].Tags![0].Name);
        Assert.AreEqual(0, (await SearchAsync(Ask("readme"))).Results.Total, "archived");
        Assert.AreEqual(1, (await SearchAsync(Ask("readme t:archived"))).Results.Total);
        Assert.AreEqual(Files + 1, (await SearchAsync(Ask("special:untagged"))).Results.Total);
    }

    [TestMethod]
    public async Task TheCoverageSaysHowMuchTheIndexHolds()
    {
        Assert.AreEqual(RemoteSearchCoverage.Complete, (await SearchAsync(Ask("x"))).Coverage);

        var none = await SearchAsync(Ask("holiday") with { Target = Target with { AccountKey = "other" } });
        Assert.AreEqual(RemoteSearchCoverage.None, none.Coverage);
        Assert.AreEqual(0, none.Results.Total);

        await UpdateStateAsync(s => s.SetProperty(i => i.Phase, RemoteIndexPhase.Listing));
        var listing = await SearchAsync(Ask("holiday"));
        Assert.AreEqual(RemoteSearchCoverage.Partial, listing.Coverage);
        Assert.AreEqual(14, listing.Results.Total, "what the index holds");

        var old = DateTime.UtcNow - RemoteIndexListings.MaxAge - TimeSpan.FromMinutes(1);
        await UpdateStateAsync(s =>
            s.SetProperty(i => i.Phase, RemoteIndexPhase.Following).SetProperty(i => i.LastSync, old)
        );
        Assert.AreEqual(RemoteSearchCoverage.Partial, (await SearchAsync(Ask("holiday"))).Coverage);
    }

    [TestMethod]
    public async Task ACursorOfAnotherSortIsRefused()
    {
        Assert.IsNull(await Search.SearchAsync(Ask("") with { After = "no-slash", Sort = SortField.Size }, Token));
        Assert.IsNull(await Search.SearchAsync(Ask("") with { After = "x/Big/a", Sort = SortField.Size }, Token));
    }

    private RemoteSearchAsk Ask(string query) => new() { Target = Target, Query = query };

    private async Task<RemoteSearchPage> SearchAsync(RemoteSearchAsk ask) =>
        await Search.SearchAsync(ask, Token) ?? throw new AssertFailedException("refused");

    private async Task<List<string>> PagesAsync(RemoteSearchAsk ask)
    {
        var paths = new List<string>();
        for (var page = 0; page < 50; page++)
        {
            var read = await SearchAsync(ask);
            paths.AddRange(read.Results.Items.Select(i => i.Path));
            if (read.Results.Next is not { } next)
                return paths;
            ask = ask with { After = next };
        }
        throw new AssertFailedException("the pages never end");
    }

    private SfeDbContext Db() => _services.Get<IDbContextFactory<SfeDbContext>>().CreateDbContext();

    private async Task TagAsync(string key, int tagId)
    {
        await using var db = Db();
        var file = await db.RemoteFiles.SingleAsync(f => f.FullName == key, Token);
        db.RemoteFileTags.Add(new RemoteFileTag { FileId = file.Id, TagId = tagId });
        await db.SaveChangesAsync(Token);
    }

    private async Task UpdateFilesAsync(
        string key,
        Action<Microsoft.EntityFrameworkCore.Query.UpdateSettersBuilder<RemoteFile>> set
    )
    {
        await using var db = Db();
        Assert.AreEqual(1, await db.RemoteFiles.Where(f => f.FullName == key).ExecuteUpdateAsync(set, Token), key);
    }

    private async Task UpdateStateAsync(
        Action<Microsoft.EntityFrameworkCore.Query.UpdateSettersBuilder<RemoteIndexState>> set
    )
    {
        await using var db = Db();
        await db.RemoteIndexes.ExecuteUpdateAsync(set, Token);
    }
}
