using System.Net;
using System.Net.Http.Json;
using Microsoft.EntityFrameworkCore;
using Otools.Net.Sfe.Api;
using Otools.Net.Sfe.Api.Endpoints;
using Otools.Net.Sfe.Core.Connections;
using Otools.Net.Sfe.Core.Connections.Indexing;
using Otools.Net.Sfe.Core.Data;
using Otools.Net.Sfe.Core.Tagging;
using Otools.Net.Sfe.Tests.Infrastructure;

namespace Otools.Net.Sfe.Tests;

/// <summary>
/// <c>api/connections/{id}/file-tags…</c> on a connection with folder rules (<see cref="RuledConnection"/>): files
/// tagged where the rules let bob tag (a folder he only reads too), their tags in listings and searches; hidden files
/// neither tagged nor counted, their tags never shown; a file the index doesn't hold yet asked of the provider first;
/// a connection never indexed answers 409; an account's own connection's tags are its owner's only (never a library
/// administrator's, whose tag counts are the library's files').
/// </summary>
[TestClass]
public sealed class ConnectionTagRouteTests
{
    private RuledConnection _world = null!;
    private int _cat;
    private int _dog;

    public TestContext TestContext { get; set; } = null!;

    private CancellationToken Token => TestContext.CancellationToken;

    private HttpClient Bob => _world.Bob;

    [TestInitialize]
    public async Task InitializeAsync()
    {
        _world = await RuledConnection.CreateAsync(Token);
        var tags = await _world.LibraryAsync<TagService>(Token);
        _cat = (await tags.CreateAsync(new TagInput("Cat"), Token)).Tag!.Id;
        _dog = (await tags.CreateAsync(new TagInput("Dog"), Token)).Tag!.Id;
    }

    [TestCleanup]
    public async Task CleanupAsync() => await _world.DisposeAsync();

    [TestMethod]
    public async Task AFileTaggedShowsItsTagsInTheDialogTheListingAndTheSearch()
    {
        var changed = await ChangeAsync(new FileTagChange(["Public/a.txt", "ReadOnly/r.txt"], Add: [_cat]));
        var tags = await GetAsync<FileTags>($"{_world.Base}/file-tags?path=public/A.TXT");
        var listing = await GetAsync<ConnectionListing>($"{_world.Base}/browse?path=Public");
        var found = await GetAsync<ConnectionSearchResults>($"{_world.Base}/search?q=t:cat");

        Assert.AreEqual(2, changed.Added, "a folder only read is tagged too: tags change no file there");
        Assert.AreEqual("Cat", tags.Tags.Single().Name);
        Assert.AreEqual("Public/a.txt", tags.Path);
        Assert.AreEqual("Cat", listing.Listing.Items.Single(i => i.Name == "a.txt").Tags!.Single().Name);
        Assert.IsNull(listing.Listing.Items.Single(i => i.Name == "b.jpg").Tags);
        CollectionAssert.AreEquivalent(
            new[] { "Public/a.txt", "ReadOnly/r.txt" },
            found.Results.Items.Select(i => i.Path).ToList()
        );
        Assert.IsEmpty(_world.Account.Calls, "the index answers alone");
    }

    [TestMethod]
    public async Task HiddenFilesAreNeitherTaggedNorCountedNorShown()
    {
        await TagHiddenAsync();

        using var tagging = await PostAsync(
            "file-tags",
            new FileTagChange(["Public/a.txt", "Private/secret.txt"], [_dog])
        );
        using var reading = await Bob.GetAsync($"{_world.Base}/file-tags?path=Private/secret.txt", Token);
        using var scope = await Bob.GetAsync($"{_world.Base}/file-tags/scope?path=Private&recursive=true", Token);
        var all = await GetAsync<ScopeTags>($"{_world.Base}/file-tags/scope?recursive=true");
        var changed = await ChangeScopeAsync(new ScopeTagChange("", Recursive: true, Add: [_dog]));

        Assert.AreEqual(HttpStatusCode.BadRequest, tagging.StatusCode, "nothing changes");
        Assert.AreEqual(HttpStatusCode.NotFound, reading.StatusCode);
        Assert.AreEqual(HttpStatusCode.NotFound, scope.StatusCode);
        Assert.AreEqual(5, all.Files, "Public (2), Mixed/ok, ReadOnly/r, Lead/Open/o");
        Assert.IsEmpty(all.Tags, "the hidden file's tag is not counted");
        Assert.AreEqual((5, 5), (changed.Files, changed.Added));
        Assert.AreEqual(5, await CountAsync(_dog), "no hidden file got it");
    }

    [TestMethod]
    public async Task AScopeOfAFolderOrASearchCountsAndChangesItsIndexedFiles()
    {
        await ChangeAsync(new FileTagChange(["Public/a.txt"], Add: [_cat]));

        var folder = await GetAsync<ScopeTags>($"{_world.Base}/file-tags/scope?path=Public");
        var search = await GetAsync<ScopeTags>($"{_world.Base}/file-tags/scope?q=t:cat");
        var removed = await ChangeScopeAsync(new ScopeTagChange("Public", Remove: [_cat]));

        Assert.AreEqual((2, 1), (folder.Files, folder.Tags.Single().FileCount));
        Assert.AreEqual(1, search.Files);
        Assert.AreEqual("t:cat", search.Scope.Query);
        Assert.AreEqual((2, 1), (removed.Files, removed.Removed));
    }

    [TestMethod]
    public async Task AFileNotIndexedYetIsAskedOfTheProviderThenTagged()
    {
        _world.Account.WithFile("Public/new.txt", "new");

        var changed = await ChangeAsync(new FileTagChange(["Public/new.txt"], Add: [_cat]));
        using var missing = await PostAsync("file-tags", new FileTagChange(["Public/none.txt"], [_cat]));

        Assert.AreEqual("Cat", changed.Files.Single().Tags.Single().Name);
        Assert.IsTrue(_world.Called("new.txt"));
        Assert.AreEqual(HttpStatusCode.BadRequest, missing.StatusCode);
    }

    [TestMethod]
    public async Task AConnectionTheLibraryNeverIndexedAnswersThatItIsNotIndexed()
    {
        var factory = await _world.LibraryAsync<IDbContextFactory<SfeDbContext>>(Token);
        await using (var db = await factory.CreateDbContextAsync(Token))
            await db.RemoteIndexes.ExecuteDeleteAsync(Token);

        using var answer = await PostAsync("file-tags", new FileTagChange(["Public/a.txt"], [_cat]));

        Assert.AreEqual(HttpStatusCode.Conflict, answer.StatusCode);
        StringAssert.Contains(await answer.Content.ReadAsStringAsync(Token), ProblemTypes.ConnectionNotIndexed);
    }

    [TestMethod]
    public async Task AnAccountsOwnConnectionsTagsAreItsOwnersOnly()
    {
        var id = await BobsOwnConnectionAsync();
        var own = $"{V1}/connections/{id}";
        using var tagged = await Bob.PostAsJsonAsync(
            $"{own}/file-tags",
            new FileTagChange(["Mine.txt"], [_dog]),
            RuledConnection.Json,
            Token
        );
        Assert.AreEqual(HttpStatusCode.OK, tagged.StatusCode, await tagged.Content.ReadAsStringAsync(Token));
        using var admin = await ApiClients.SignInAsync(
            _world.Factory,
            ApiClients.AdminName,
            ApiClients.AdminPassword,
            Token
        );

        using var tags = await admin.GetAsync($"{own}/file-tags?path=Mine.txt", Token);
        using var scope = await admin.GetAsync($"{own}/file-tags/scope", Token);
        using var change = await admin.PostAsJsonAsync($"{own}/file-tags/scope", new ScopeTagChange(""), Token);
        var dog = (await admin.GetFromJsonAsync<TagDetails>($"{V1}/tags/{_dog}", RuledConnection.Json, Token))!;

        Assert.AreEqual(HttpStatusCode.NotFound, tags.StatusCode);
        Assert.AreEqual(HttpStatusCode.NotFound, scope.StatusCode);
        Assert.AreEqual(HttpStatusCode.NotFound, change.StatusCode);
        Assert.AreEqual(0, dog.FileCount, "tag counts count the library's files only");
    }

    /// <summary>Private/secret.txt tagged Cat, as another session with the right to see it could have.</summary>
    private async Task TagHiddenAsync()
    {
        var tags = await _world.LibraryAsync<RemoteFileTags>(Token);
        var place = new RemoteTagPlace(_world.Id, _world.Provider.Capabilities.CaseInsensitiveNames);
        Assert.IsTrue(await tags.AddAsync(place, "Private/secret.txt", [_cat], Token));
    }

    /// <summary>An account-level connection of bob's, indexed in the library.</summary>
    private async Task<int> BobsOwnConnectionAsync()
    {
        _world.Provider.Account("bobs").WithFile("Mine.txt", "mine");
        var draft = new ConnectionDraft(FakeProvider.FakeKind, "Bob's", ConnectionLevel.Account, OwnerId: _world.BobId);
        var grant = new ConnectionGrant("bobs", "bobs@fake", "id:bobs");
        var made = await _world.Get<ConnectionService>().CreateAsync(draft, grant, _world.BobId, Token);
        var id = made.Value!.Id;
        var indexes = await _world.LibraryAsync<ConnectionIndexes>(Token);
        Assert.IsTrue(await indexes.StartAsync(id, Otools.Net.Sfe.Core.Tasks.TaskTrigger.Manual, null));
        var until = DateTime.UtcNow.AddSeconds(10);
        while ((await indexes.StatusAsync(id, Token)) is { Running: true } or { LastSync: null })
        {
            if (DateTime.UtcNow > until)
                throw new AssertFailedException("The connection's index was not synced.");
            await Task.Delay(20, Token);
        }
        return id;
    }

    private async Task<int> CountAsync(int tagId)
    {
        var factory = await _world.LibraryAsync<IDbContextFactory<SfeDbContext>>(Token);
        await using var db = await factory.CreateDbContextAsync(Token);
        return await db.RemoteFileTags.CountAsync(l => l.TagId == tagId, Token);
    }

    private Task<HttpResponseMessage> PostAsync(string route, object body) =>
        Bob.PostAsJsonAsync($"{_world.Base}/{route}", body, RuledConnection.Json, Token);

    private async Task<FileTagChangeResult> ChangeAsync(FileTagChange change)
    {
        using var answer = await PostAsync("file-tags", change);
        Assert.AreEqual(HttpStatusCode.OK, answer.StatusCode, await answer.Content.ReadAsStringAsync(Token));
        return (await answer.Content.ReadFromJsonAsync<FileTagChangeResult>(RuledConnection.Json, Token))!;
    }

    private async Task<ScopeTagChangeResult> ChangeScopeAsync(ScopeTagChange change)
    {
        using var answer = await PostAsync("file-tags/scope", change);
        Assert.AreEqual(HttpStatusCode.OK, answer.StatusCode, await answer.Content.ReadAsStringAsync(Token));
        return (await answer.Content.ReadFromJsonAsync<ScopeTagChangeResult>(RuledConnection.Json, Token))!;
    }

    private async Task<T> GetAsync<T>(string url)
    {
        using var answer = await Bob.GetAsync(url, Token);
        Assert.AreEqual(HttpStatusCode.OK, answer.StatusCode, await answer.Content.ReadAsStringAsync(Token));
        return (await answer.Content.ReadFromJsonAsync<T>(RuledConnection.Json, Token))!;
    }
}
