using Otools.Net.Sfe.Client.Tests.Infrastructure;

namespace Otools.Net.Sfe.Client.Tests.Facade;

/// <summary>
/// <see cref="SfeSearch"/> against the real server, on its indexed test library: queries in the app's language,
/// sorts, cursor paging, a folder, the count.
/// </summary>
[TestClass]
public sealed class SearchTests
{
    public TestContext TestContext { get; set; } = null!;

    private CancellationToken Token => TestContext.CancellationToken;

    [TestMethod]
    public async Task AQueryInTheAppsLanguageFindsTheIndexedFiles()
    {
        using var server = await TokenServer.StartAsync(Token);
        await server.IndexAsync(Token);
        using var sfe = server.CreateClient();

        var found = await sfe.Search.QueryAsync("episode e:mkv", Token).ToListAsync(Token);

        CollectionAssert.AreEqual(
            new[] { "Media/Episode 10.mkv", "Media/Episode 2.mkv" },
            found.Select(item => item.Path).ToArray()
        );
        Assert.AreEqual(SfeItemKind.Video, found[0].Kind);
        Assert.AreEqual(300 * 1024, found[0].Size);
    }

    [TestMethod]
    public async Task ResultsComePageByPageInTheOrderAsked()
    {
        using var server = await TokenServer.StartAsync(Token);
        await server.IndexAsync(Token);
        using var sfe = server.CreateClient();
        var options = new SfeSearchOptions
        {
            Sort = SfeSearchSort.Size,
            Descending = true,
            PageSize = 3,
        };

        var found = await sfe.Search.QueryAsync("", options, Token).ToListAsync(Token);
        var count = await sfe.Search.CountAsync("", cancellationToken: Token);

        Assert.HasCount(count.Count, found);
        Assert.IsGreaterThan(options.PageSize * 3, found.Count);
        Assert.AreEqual(found.Count, found.Select(item => item.Path).Distinct().Count());
        Assert.AreEqual("Media/Holiday 2020.mp4", found[0].Path);
        var sizes = found.Select(item => item.Size ?? 0).ToList();
        CollectionAssert.AreEqual(sizes.OrderByDescending(size => size).ToList(), sizes);
    }

    [TestMethod]
    public async Task AFolderLimitsTheSearchToItsTree()
    {
        using var server = await TokenServer.StartAsync(Token);
        await server.IndexAsync(Token);
        using var sfe = server.CreateClient();
        var options = new SfeSearchOptions { Folder = "RepeatedDir/" };

        var found = await sfe.Search.QueryAsync("RDFile1", options, Token).ToListAsync(Token);

        CollectionAssert.AreEqual(
            new[] { "RepeatedDir/RDFile1.txt", "RepeatedDir/RepeatedDir/RDFile1.txt" },
            found.Select(item => item.Path).ToArray()
        );
    }

    [TestMethod]
    public async Task TheFirstPageCountsTheMatchesAndGivesTheNextCursor()
    {
        using var server = await TokenServer.StartAsync(Token);
        await server.IndexAsync(Token);
        using var sfe = server.CreateClient();
        var options = new SfeSearchOptions { Sort = SfeSearchSort.Name, PageSize = 2 };

        var first = await sfe.Search.GetPageAsync("e:txt", options, cancellationToken: Token);
        var second = await sfe.Search.GetPageAsync("e:txt", options, first.Next, Token);
        var count = await sfe.Search.CountAsync("e:txt", cancellationToken: Token);

        var texts = Directory.GetFiles(server.Library.Root, "*.txt", SearchOption.AllDirectories).Length;
        Assert.AreEqual(texts, first.Total);
        Assert.IsFalse(first.TotalCapped);
        Assert.IsTrue(first.HasMore);
        Assert.IsNull(second.Total);
        Assert.HasCount(2, second.Items);
        Assert.IsEmpty(first.Items.Select(item => item.Path).Intersect(second.Items.Select(item => item.Path)));
        Assert.AreEqual(texts, count.Count);
        Assert.IsFalse(count.Capped);
    }

    [TestMethod]
    public async Task ASearchFindingNothingIsEmpty()
    {
        using var server = await TokenServer.StartAsync(Token);
        await server.IndexAsync(Token);
        using var sfe = server.CreateClient();
        var options = new SfeSearchOptions { Folder = "Nowhere" };

        var nowhere = await sfe.Search.QueryAsync("", options, Token).ToListAsync(Token);
        var nothing = await sfe.Search.QueryAsync("no-such-name", Token).ToListAsync(Token);
        var count = await sfe.Search.CountAsync("no-such-name", cancellationToken: Token);

        Assert.IsEmpty(nowhere);
        Assert.IsEmpty(nothing);
        Assert.AreEqual(0, count.Count);
    }
}
