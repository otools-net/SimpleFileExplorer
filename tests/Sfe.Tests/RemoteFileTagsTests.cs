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
/// The library's tags on a connection's files (<see cref="RemoteFileTags"/>, <see cref="RemoteScopeTagger"/>): a
/// folder's files, its tree or a search's matches counted and changed by batches, where the asker sees and may tag
/// only; an index of another account holding none; listed files given their tags; a deleted tag taken off every
/// connection file in batches; the search's tag term read from the links' index.
/// </summary>
[TestClass]
public sealed class RemoteFileTagsTests
{
    private const int Id = 7;

    private readonly FakeProvider _provider = new();
    private TestLibrary _library = null!;
    private CoreServices _services = null!;
    private int _cat;
    private int _dog;

    public TestContext TestContext { get; set; } = null!;

    private CancellationToken Token => TestContext.CancellationToken;

    private ConnectionIndexTarget Target => new(Id, "", "account", _provider.Capabilities, false);

    private RemoteScopeTagger Tagger => _services.Get<RemoteScopeTagger>();

    private RemoteFileTags Tags => _services.Get<RemoteFileTags>();

    [TestInitialize]
    public async Task InitializeAsync()
    {
        _library = new TestLibrary();
        _services = await CoreServices.CreateAsync(_library);
        var account = _provider.Account("acct");
        for (var i = 1; i <= 4; i++)
            account.WithFile($"Big/Holiday {i}.jpg", "x");
        account.WithFile("Big/Sub/Deep holiday.jpg", "deep").WithFile("Readme.txt", "read me");
        var storage = new FakeStorage(account, "", _provider.Capabilities);
        await _services.Get<ConnectionIndexer>().SyncAsync(Target, new RecordingIndexSource(storage), new(), Token);
        var tags = _services.Get<TagService>();
        _cat = (await tags.CreateAsync(new TagInput("Cat"), Token)).Tag!.Id;
        _dog = (await tags.CreateAsync(new TagInput("Dog"), Token)).Tag!.Id;
    }

    [TestCleanup]
    public async Task CleanupAsync()
    {
        await _services.DisposeAsync();
        SqliteConnection.ClearAllPools();
        _library.Dispose();
    }

    [TestMethod]
    public async Task AFolderCountsItsOwnFilesOrItsTreeAndASearchItsMatches()
    {
        await TagAsync(_cat, "Big/Holiday 1.jpg", "Big/Sub/Deep holiday.jpg");

        var own = await Tagger.GetTagsAsync(Scope(new TagScope("Big")), Token);
        var tree = await Tagger.GetTagsAsync(Scope(new TagScope("Big", Recursive: true)), Token);
        var found = await Tagger.GetTagsAsync(Scope(new TagScope("", Query: "deep")), Token);

        Assert.AreEqual((4, 1), (own.Files, own.Tags.Single().FileCount));
        Assert.AreEqual((5, 2), (tree.Files, tree.Tags.Single().FileCount));
        Assert.AreEqual((1, "Cat"), (found.Files, found.Tags.Single().Tag.Name));
        Assert.AreEqual(new TagScope("", false, "deep"), found.Scope);
    }

    [TestMethod]
    public async Task AChangeTagsTheFilesWhereTheAskerMayTagAndNoneItDoesntSee()
    {
        var scope = Scope(new TagScope("", Recursive: true)) with
        {
            Visible = new PathFilter([new PathRegion("Big", [])]),
            Taggable = new PathFilter([new PathRegion("", ["Big/Sub"])]),
        };

        var outcome = await Tagger.ChangeAsync(scope, [_dog], null, Token);
        var counted = await Tagger.GetTagsAsync(scope, Token);

        Assert.AreEqual(ScopeTagStatus.Ok, outcome.Status);
        Assert.AreEqual((4, 4, 0), (outcome.Result!.Files, outcome.Result.Added, outcome.Result.Removed));
        Assert.AreEqual((5, 4), (counted.Files, counted.Tags.Single().FileCount), "Readme.txt is not seen");
        CollectionAssert.AreEquivalent(
            new[] { "big/holiday 1.jpg", "big/holiday 2.jpg", "big/holiday 3.jpg", "big/holiday 4.jpg" },
            await TaggedAsync(_dog)
        );
        Assert.IsEmpty(await TagIdsAsync("Big/Sub/Deep holiday.jpg"));

        var removed = await Tagger.ChangeAsync(Scope(new TagScope("Big")), null, [_dog], Token);
        Assert.AreEqual(4, removed.Result!.Removed);
    }

    [TestMethod]
    public async Task UnknownTagsAndEmptyChangesAreRefused()
    {
        var none = await Tagger.ChangeAsync(Scope(new TagScope("Big")), null, null, Token);
        var unknown = await Tagger.ChangeAsync(Scope(new TagScope("Big")), [999], null, Token);

        Assert.AreEqual(ScopeTagStatus.Invalid, none.Status);
        Assert.AreEqual("Tag 999 does not exist.", unknown.Errors!["add"][0]);
    }

    [TestMethod]
    public async Task AnIndexOfAnotherAccountHoldsNoFileToTagNorItsTags()
    {
        await TagAsync(_cat, "Big/Holiday 1.jpg");
        var other = Target with { AccountKey = "another" };

        var counted = await Tagger.GetTagsAsync(Scope(new TagScope("Big")) with { Target = other }, Token);
        var changed = await Tagger.ChangeAsync(Scope(new TagScope("Big")) with { Target = other }, [_dog], null, Token);
        var rows = await Tags.RowsAsync(RemoteTagPlace.Of(other), ["Big/Holiday 1.jpg"], Token);

        Assert.AreEqual(0, counted.Files);
        Assert.AreEqual(0, changed.Result!.Files);
        Assert.IsEmpty(rows);
    }

    [TestMethod]
    public async Task ListedFilesGetTheirTagsAndAMovedSourceLosesThem()
    {
        var place = RemoteTagPlace.Of(Target);
        Assert.IsTrue(await Tags.AddAsync(place, "BIG/holiday 2.jpg", [_cat, 999], Token), "unknown tags left out");
        Assert.IsFalse(await Tags.AddAsync(place, "Big/Missing.jpg", [_cat], Token), "no row, no tags");
        LibraryItem[] items =
        [
            new("Holiday 2.jpg", "Big/Holiday 2.jpg", ItemKind.Image, ".jpg", 1, null),
            new("Holiday 3.jpg", "Big/Holiday 3.jpg", ItemKind.Image, ".jpg", 1, null),
            new("Sub", "Big/Sub", ItemKind.Directory, null, null, null),
        ];

        var listed = await Tags.AttachAsync(place, items, Token);
        CollectionAssert.AreEqual(new[] { "Cat" }, listed[0].Tags!.Select(t => t.Name).ToList());
        Assert.IsNull(listed[1].Tags);
        Assert.IsNull(listed[2].Tags);
        CollectionAssert.AreEqual(new[] { _cat }, (await Tags.TagIdsAsync(place, "Big/Holiday 2.jpg", Token)).ToList());

        await MarkRemovedAsync("big/holiday 2.jpg");
        await Tags.ForgetAsync(place, "Big/Holiday 2.jpg", Token);
        Assert.IsEmpty(await TagIdsAsync("Big/Holiday 2.jpg"), "a removed row's tags go too");
    }

    [TestMethod]
    public async Task DeletingATagTakesItOffEveryConnectionFileByBatches()
    {
        await AddRowsAsync(RemoteFileTagRows.BatchSize + 300, _cat);
        await TagAsync(_dog, "Big/Holiday 1.jpg");

        Assert.AreEqual(TagDeleteStatus.Deleted, await _services.Get<TagService>().DeleteAsync(_cat, Token));

        await using var db = Db();
        Assert.AreEqual(0, await db.RemoteFileTags.CountAsync(l => l.TagId == _cat, Token));
        Assert.AreEqual(1, await db.RemoteFileTags.CountAsync(l => l.TagId == _dog, Token), "other tags stay");
    }

    [TestMethod]
    public async Task TheSearchsTagTermReadsTheLinksIndex()
    {
        await TagAsync(_cat, "Big/Holiday 3.jpg");
        await using var db = Db();
        var ask = new RemoteSearchAsk { Target = Target, Query = "t:cat -t:dog" };

        var matches = await _services.Get<RemoteIndexSearch>().MatchesAsync(db, ask, Token);
        var sql = matches.ToQueryString();

        Assert.AreEqual("big/holiday 3.jpg", (await matches.SingleAsync(Token)).FullName);
        Assert.Contains("\"Id\" IN (", sql);
        Assert.Contains("\"Id\" NOT IN (", sql);
        Assert.DoesNotContain("EXISTS", sql);
    }

    private RemoteTagScope Scope(TagScope scope) => new() { Target = Target, Scope = scope };

    private SfeDbContext Db() => _services.Get<IDbContextFactory<SfeDbContext>>().CreateDbContext();

    private async Task TagAsync(int tagId, params string[] paths)
    {
        var rows = await Tags.RowsAsync(RemoteTagPlace.Of(Target), paths, Token);
        Assert.HasCount(paths.Length, rows);
        await using var db = Db();
        db.RemoteFileTags.AddRange(rows.Select(r => new RemoteFileTag { FileId = r.Id, TagId = tagId }));
        await db.SaveChangesAsync(Token);
    }

    private async Task<List<string>> TaggedAsync(int tagId)
    {
        await using var db = Db();
        return await db.RemoteFileTags.Where(l => l.TagId == tagId).Select(l => l.File!.FullName).ToListAsync(Token);
    }

    private async Task<List<int>> TagIdsAsync(string path)
    {
        await using var db = Db();
        var key = path.ToLowerInvariant();
        return await db.RemoteFileTags.Where(l => l.File!.FullName == key).Select(l => l.TagId).ToListAsync(Token);
    }

    private async Task MarkRemovedAsync(string key)
    {
        await using var db = Db();
        await db
            .RemoteFiles.Where(f => f.FullName == key)
            .ExecuteUpdateAsync(s => s.SetProperty(f => f.Removed, true), Token);
    }

    /// <summary>Rows of more files in <c>Big</c>, each with <paramref name="tagId"/>.</summary>
    private async Task AddRowsAsync(int count, int tagId)
    {
        await using var db = Db();
        var folder = await db.RemoteDirectories.SingleAsync(d => d.FullName == "big", Token);
        for (var i = 0; i < count; i++)
        {
            var file = new RemoteFile
            {
                ConnectionId = Id,
                DirectoryId = folder.Id,
                FullName = $"big/more {i}.txt",
                DisplayPath = $"Big/More {i}.txt",
                Name = $"More {i}",
                Extension = ".txt",
            };
            file.TagLinks.Add(new RemoteFileTag { TagId = tagId });
            db.RemoteFiles.Add(file);
        }
        await db.SaveChangesAsync(Token);
    }
}
