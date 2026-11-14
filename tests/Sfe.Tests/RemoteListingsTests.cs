using Sfe.Core.Connections;
using Sfe.Core.Library;
using Sfe.Tests.Infrastructure;

namespace Sfe.Tests;

/// <summary>
/// <see cref="RemoteListings"/>: a folder read once and sorted, the next pages taken from that read while it is
/// recent, a folder past the cap listed in the provider's order through a bounded window.
/// </summary>
[TestClass]
public sealed class RemoteListingsTests
{
    private readonly FakeProvider _provider = new();
    private readonly SteppedClock _clock = new();
    private FakeAccount _account = null!;
    private IRemoteStorage _storage = null!;

    public TestContext TestContext { get; set; } = null!;

    private CancellationToken Token => TestContext.CancellationToken;

    [TestInitialize]
    public void Initialize()
    {
        _account = _provider
            .Account("alice")
            .WithFile("b.txt", "bb")
            .WithFolder("Zoo")
            .WithFile("a.jpg", "a")
            .WithFile("c.mp4", "ccc");
        _storage = _provider.Open("alice", "", new());
    }

    [TestCleanup]
    public async Task CleanupAsync() => await _storage.DisposeAsync();

    [TestMethod]
    public async Task AFolderIsReadOnceSortedAndPagedFromThatRead()
    {
        var listings = new RemoteListings(_clock);

        var first = await listings.ListAsync(_storage, Query(0, 2), Token);
        var calls = _account.Calls.Count;
        var second = await listings.ListAsync(_storage, Query(2, 2), Token);

        CollectionAssert.AreEqual(new[] { "Zoo", "a.jpg" }, first.Items.Select(i => i.Name).ToArray());
        CollectionAssert.AreEqual(new[] { "b.txt", "c.mp4" }, second.Items.Select(i => i.Name).ToArray());
        Assert.AreEqual((4, 1, true, false), (first.Total, first.DirectoryCount, first.Sorted, first.HasMore));
        Assert.HasCount(calls, _account.Calls, "the second page came from the first read");

        var bySize = await listings.ListAsync(
            _storage,
            Query(0, 4) with
            {
                Sort = SortField.Size,
                Descending = true,
            },
            Token
        );
        CollectionAssert.AreEqual(
            new[] { "Zoo", "c.mp4", "b.txt", "a.jpg" },
            bySize.Items.Select(i => i.Name).ToArray()
        );
    }

    [TestMethod]
    public async Task TheFirstPageAndAnOldReadListAgain()
    {
        var listings = new RemoteListings(_clock);
        await listings.ListAsync(_storage, Query(0, 2), Token);
        _account.WithFile("aa.txt", "new");

        var later = await listings.ListAsync(_storage, Query(2, 2), Token);
        Assert.AreEqual(4, later.Total, "within its lifetime, the read stands");

        _clock.Now += RemoteListings.Lifetime + TimeSpan.FromSeconds(1);
        var old = await listings.ListAsync(_storage, Query(2, 2), Token);
        Assert.AreEqual(5, old.Total);

        _account.WithFile("ab.txt", "newer");
        Assert.AreEqual(6, (await listings.ListAsync(_storage, Query(0, 2), Token)).Total);

        listings.Forget(1);
        _account.WithFile("ac.txt", "newest");
        Assert.AreEqual(7, (await listings.ListAsync(_storage, Query(1, 2), Token)).Total, "forgotten: read anew");
    }

    [TestMethod]
    public async Task AFolderPastTheCapComesInTheProvidersOrderThroughAWindow()
    {
        for (var n = 0; n < 6; n++)
            _account.WithFile($"more-{n}.txt", "x");
        // Ten items, pages of two, a cap of four.
        var listings = new RemoteListings(_clock, maxItems: 4);

        var first = await listings.ListAsync(_storage, Query(0, 3), Token);
        Assert.AreEqual((4, false, true), (first.Total, first.Sorted, first.HasMore));
        Assert.HasCount(3, first.Items);

        var further = await listings.ListAsync(_storage, Query(6, 3), Token);
        Assert.HasCount(3, further.Items);
        Assert.IsGreaterThanOrEqualTo(9, further.Total);

        var last = await listings.ListAsync(_storage, Query(9, 3), Token);
        Assert.AreEqual((10, false), (last.Total, last.HasMore));
        Assert.HasCount(1, last.Items);

        // Every item once, in the provider's order, across the pages read.
        var back = await listings.ListAsync(_storage, Query(3, 3), Token);
        var all = first.Items.Concat(back.Items).Concat(further.Items).Concat(last.Items).Select(i => i.Name);
        Assert.HasCount(10, all.Distinct().ToList());
    }

    private static RemoteListingQuery Query(int offset, int limit) => new(1, "", offset, limit);

    private sealed class SteppedClock : TimeProvider
    {
        public DateTimeOffset Now { get; set; } = new(2026, 11, 14, 12, 0, 0, TimeSpan.Zero);

        public override DateTimeOffset GetUtcNow() => Now;
    }
}
