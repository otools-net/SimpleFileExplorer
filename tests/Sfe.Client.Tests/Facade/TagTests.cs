using Otools.Net.Sfe.Client.Tests.Infrastructure;

namespace Otools.Net.Sfe.Client.Tests.Facade;

/// <summary>
/// <see cref="SfeTags"/> against the real server: tags made, found, changed and deleted; given to files and taken
/// from them, in batches for many files.
/// </summary>
[TestClass]
public sealed class TagTests
{
    private const string Holiday = "Media/Holiday 2020.mp4";
    private const string Photo = "Media/photo.jpg";

    public TestContext TestContext { get; set; } = null!;

    private CancellationToken Token => TestContext.CancellationToken;

    [TestMethod]
    public async Task ATagIsMadeThenListedFoundAndRead()
    {
        using var server = await TokenServer.StartAsync(Token);
        using var sfe = server.CreateClient();
        var parent = await sfe.Tags.CreateAsync(new SfeTagDraft { Name = "Travel" }, Token);
        var draft = new SfeTagDraft
        {
            Name = "Vacation",
            Color = "teal",
            Aliases = ["Holiday trip"],
            ParentIds = [parent.Id],
        };

        var created = await sfe.Tags.CreateAsync(draft, Token);
        var listed = await sfe.Tags.ListAsync(Token);
        var found = await sfe.Tags.FindAsync("holiday", cancellationToken: Token);
        var descendants = await sfe.Tags.FindAsync("travel", cancellationToken: Token);
        var read = await sfe.Tags.GetAsync(created.Id, Token);

        Assert.AreEqual("teal", created.Color);
        Assert.IsTrue(listed.Any(tag => tag.Id == created.Id));
        Assert.AreEqual(created.Id, found.Matches.Single().Id);
        Assert.AreEqual(created.Id, descendants.Descendants.Single().Id);
        CollectionAssert.AreEqual(new[] { "Holiday trip" }, read.Aliases.ToArray());
        Assert.AreEqual("Travel", read.Parents.Single().Name);
        Assert.AreEqual(0, read.FileCount);
    }

    [TestMethod]
    public async Task TagsAreGivenToFilesAndTakenFromThem()
    {
        using var server = await TokenServer.StartAsync(Token);
        using var sfe = server.CreateClient();
        var tag = await sfe.Tags.CreateAsync(new SfeTagDraft { Name = "Vacation" }, Token);

        var added = await sfe.Tags.AddAsync([Holiday, Photo], [tag.Id], Token);
        var listing = await sfe.Files.ListAsync("Media", Token).ToListAsync(Token);
        var found = await sfe.Search.QueryAsync("t:vacation", Token).ToListAsync(Token);
        var removed = await sfe.Tags.RemoveAsync([Photo], [tag.Id], Token);
        var holiday = await sfe.Tags.GetFileTagsAsync(Holiday, Token);
        var photo = await sfe.Tags.GetFileTagsAsync(Photo, Token);

        Assert.AreEqual(2, added.Added);
        Assert.AreEqual("Vacation", added.Files.Single(file => file.Path == Holiday).Tags.Single().Name);
        Assert.AreEqual(tag.Id, listing.Single(item => item.Path == Holiday).Tags!.Single().Id);
        CollectionAssert.AreEquivalent(new[] { Holiday, Photo }, found.Select(item => item.Path).ToArray());
        Assert.AreEqual(1, removed.Removed);
        Assert.AreEqual(tag.Id, holiday.Tags.Single().Id);
        Assert.IsNull(holiday.Groups.Single().Category);
        Assert.IsEmpty(photo.Tags);
    }

    [TestMethod]
    public async Task ManyFilesAreTaggedInBatches()
    {
        using var server = await TokenServer.StartAsync(Token);
        var paths = Enumerable.Range(0, 1001).Select(index => $"Many/{index:D4}.txt").ToList();
        foreach (var path in paths)
            server.Library.AddFile(path, 1);
        using var sfe = server.CreateClient();
        var tag = await sfe.Tags.CreateAsync(new SfeTagDraft { Name = "Bulk" }, Token);

        var added = await sfe.Tags.AddAsync(paths, [tag.Id], Token);

        Assert.AreEqual(1001, added.Added);
        Assert.HasCount(1001, added.Files);
        Assert.AreEqual(1001, (await sfe.Tags.GetAsync(tag.Id, Token)).FileCount);
    }

    [TestMethod]
    public async Task ATagIsChangedThenDeleted()
    {
        using var server = await TokenServer.StartAsync(Token);
        using var sfe = server.CreateClient();
        var tag = await sfe.Tags.CreateAsync(new SfeTagDraft { Name = "Vacation", Shorthand = "vac" }, Token);
        await sfe.Tags.AddAsync([Photo], [tag.Id], Token);

        var renamed = await sfe.Tags.UpdateAsync(tag.Id, new SfeTagDraft { Name = "Holidays" }, Token);
        await sfe.Tags.DeleteAsync(tag.Id, Token);

        Assert.AreEqual("Holidays", renamed.Name);
        Assert.IsNull(renamed.Shorthand);
        await Assert.ThrowsExactlyAsync<SfeNotFoundException>(() => sfe.Tags.GetAsync(tag.Id, Token));
        Assert.IsEmpty((await sfe.Tags.GetFileTagsAsync(Photo, Token)).Tags);
    }

    [TestMethod]
    public async Task AFolderOrAnUnknownTagIsRefused()
    {
        using var server = await TokenServer.StartAsync(Token);
        using var sfe = server.CreateClient();
        var tag = await sfe.Tags.CreateAsync(new SfeTagDraft { Name = "Vacation" }, Token);

        var folder = await Assert.ThrowsAsync<SfeApiException>(() => sfe.Tags.AddAsync(["Media"], [tag.Id], Token));
        var unknown = await Assert.ThrowsAsync<SfeApiException>(() => sfe.Tags.AddAsync([Photo], [tag.Id + 99], Token));

        Assert.AreEqual(400, folder.StatusCode);
        Assert.IsTrue(folder.Errors.ContainsKey("paths"));
        Assert.IsTrue(unknown.Errors.ContainsKey("add"));
    }
}
