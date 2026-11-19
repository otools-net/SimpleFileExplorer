using Otools.Net.Sfe.Client.Tests.Infrastructure;

namespace Otools.Net.Sfe.Client.Tests.Facade;

/// <summary>
/// <see cref="SfeConnectionSearch"/> and the index of a connection (<see cref="SfeConnection.GetIndexAsync"/>,
/// <see cref="SfeConnection.SyncIndexAsync"/>) on the real server with the <c>FakeProvider</c>: synced, then searched
/// in pages, sorted, in a folder, counted, without calling the provider.
/// </summary>
[TestClass]
public sealed class ConnectionSearchTests
{
    public TestContext TestContext { get; set; } = null!;

    private CancellationToken Token => TestContext.CancellationToken;

    [TestMethod]
    public async Task TheIndexIsSyncedAndTellsWhatItHolds()
    {
        using var world = await ConnectedServer.StartAsync(Token);

        var started = await world.Connection.SyncIndexAsync(Token);
        var synced = await SyncedAsync(world.Connection, Token);

        Assert.IsTrue(started.Indexed);
        Assert.AreEqual((2L, 3L), (synced.Files, synced.Folders), "Photos, Photos/Trips and the root");
        Assert.IsFalse(synced.Listing);
        Assert.IsNull(synced.LastError);
        await Assert.ThrowsExactlyAsync<SfeNotFoundException>(() =>
            world.Client.Connections[world.Id + 100].GetIndexAsync(Token)
        );
    }

    [TestMethod]
    public async Task ASyncedConnectionIsSearchedPageByPageWithoutCallingTheProvider()
    {
        using var world = await ConnectedServer.StartAsync(Token);
        world.Account.WithFile("Photos/Trips/c.jpg", "picture c").WithFile("d.jpg", "d");
        await world.Connection.SyncIndexAsync(Token);
        await SyncedAsync(world.Connection, Token);
        world.Account.Calls.Clear();
        var search = world.Connection.Search;
        var options = new SfeSearchOptions { Sort = SfeSearchSort.Name, PageSize = 2 };

        var pictures = await search.QueryAsync("e:jpg", options, Token).ToListAsync(Token);
        var first = await search.GetPageAsync("e:jpg", options, cancellationToken: Token);
        var second = await search.GetPageAsync("e:jpg", options, first.Next, Token);
        var trips = await search
            .QueryAsync("", new SfeSearchOptions { Folder = "Photos/Trips" }, Token)
            .ToListAsync(Token);
        var count = await search.CountAsync("e:jpg", "Photos", Token);

        CollectionAssert.AreEqual(
            new[] { "Photos/b.jpg", "Photos/Trips/c.jpg", "d.jpg" },
            pictures.Select(item => item.Path).ToArray()
        );
        Assert.AreEqual((3, true, SfeIndexCoverage.Complete), (first.Total, first.HasMore, first.Coverage));
        Assert.AreEqual(("d.jpg", false, (int?)null), (second.Items.Single().Path, second.HasMore, second.Total));
        Assert.AreEqual("Photos/Trips/c.jpg", trips.Single().Path);
        Assert.AreEqual((2, false, SfeIndexCoverage.Complete), (count.Count, count.Capped, count.Coverage));
        Assert.IsEmpty(world.Account.Calls, "the index answers alone");
    }

    /// <summary>The index's status once a sync has ended (10 s at most).</summary>
    internal static async Task<SfeConnectionIndex> SyncedAsync(
        SfeConnection connection,
        CancellationToken cancellationToken
    )
    {
        var until = DateTime.UtcNow.AddSeconds(10);
        while (true)
        {
            var status = await connection.GetIndexAsync(cancellationToken);
            if (status is { Running: false, LastSync: not null })
                return status;
            if (DateTime.UtcNow > until)
                throw new AssertFailedException("The connection's index was not synced.");
            await Task.Delay(20, cancellationToken);
        }
    }
}
