using Otools.Net.Sfe.Client.Tests.Infrastructure;

namespace Otools.Net.Sfe.Client.Tests.Facade;

/// <summary>
/// <see cref="SfeConnectionTags"/> on the real server with the <c>FakeProvider</c>: refused while the connection isn't
/// indexed, then tags given to files and taken, one by one and on a whole folder or search.
/// </summary>
[TestClass]
public sealed class ConnectionTagTests
{
    public TestContext TestContext { get; set; } = null!;

    private CancellationToken Token => TestContext.CancellationToken;

    [TestMethod]
    public async Task AConnectionNotIndexedYetRefusesTagsAsNotIndexed()
    {
        using var world = await ConnectedServer.StartAsync(Token);
        var tag = await world.Client.Tags.CreateAsync(new SfeTagDraft { Name = "Vacation" }, Token);

        var refused = await Assert.ThrowsExactlyAsync<SfeConflictException>(() =>
            world.Connection.Tags.AddAsync(["Photos/a.mp4"], [tag.Id], Token)
        );

        Assert.AreEqual("connection-not-indexed", refused.ProblemType);
        Assert.AreEqual(SfeConnectionProblem.NotIndexed, refused.ConnectionProblem);
    }

    [TestMethod]
    public async Task FilesAreTaggedThenFoundByTheirTags()
    {
        using var world = await IndexedAsync();
        var tags = world.Connection.Tags;
        var tag = await world.Client.Tags.CreateAsync(new SfeTagDraft { Name = "Vacation" }, Token);
        // Not in the index yet: asked of the provider on the way.
        world.Account.WithFile("Photos/new.txt", "new");

        var added = await tags.AddAsync(["Photos/a.mp4", "Photos/new.txt"], [tag.Id], Token);
        var found = await world.Connection.Search.QueryAsync("t:vacation", Token).ToListAsync(Token);
        var removed = await tags.RemoveAsync(["Photos/new.txt"], [tag.Id], Token);
        var video = await tags.GetFileTagsAsync("Photos/a.mp4", Token);
        var text = await tags.GetFileTagsAsync("Photos/new.txt", Token);

        Assert.AreEqual((2, 0), (added.Added, added.Removed));
        CollectionAssert.AreEquivalent(
            new[] { "Photos/a.mp4", "Photos/new.txt" },
            found.Select(item => item.Path).ToArray()
        );
        Assert.AreEqual(1, removed.Removed);
        Assert.AreEqual("Vacation", video.Tags.Single().Name);
        Assert.IsEmpty(text.Tags);
    }

    [TestMethod]
    public async Task AFolderOrASearchIsTaggedWhole()
    {
        using var world = await IndexedAsync();
        var tags = world.Connection.Tags;
        var tag = await world.Client.Tags.CreateAsync(new SfeTagDraft { Name = "Trip" }, Token);
        var folder = new SfeTagScope { Folder = "Photos" };
        var pictures = new SfeTagScope { Folder = "Photos", Query = "e:jpg" };

        var added = await tags.ChangeScopeAsync(folder, [tag.Id], [], Token);
        var counted = await tags.GetScopeTagsAsync(folder, Token);
        var removed = await tags.ChangeScopeAsync(pictures, [], [tag.Id], Token);
        var left = await tags.GetScopeTagsAsync(folder, Token);

        Assert.AreEqual((2, 2), (added.Files, added.Added));
        Assert.AreEqual(2, counted.Files);
        Assert.AreEqual(("Trip", 2), (counted.Tags.Single().Tag.Name, counted.Tags.Single().FileCount));
        Assert.AreEqual((1, 1), (removed.Files, removed.Removed));
        Assert.AreEqual(1, left.Tags.Single().FileCount);
        await Assert.ThrowsExactlyAsync<SfeApiException>(() =>
            tags.ChangeScopeAsync(folder, [tag.Id + 100], [], Token)
        );
    }

    /// <summary>A connected server whose connection the library has indexed.</summary>
    private async Task<ConnectedServer> IndexedAsync()
    {
        var world = await ConnectedServer.StartAsync(Token);
        await world.Connection.SyncIndexAsync(Token);
        await ConnectionSearchTests.SyncedAsync(world.Connection, Token);
        return world;
    }
}
