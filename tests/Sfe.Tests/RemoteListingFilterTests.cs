using Sfe.Core.Connections;
using Sfe.Core.Library;
using Sfe.Tests.Infrastructure;

namespace Sfe.Tests;

/// <summary>
/// <see cref="RemoteListings"/> with what folder rules hide from one subject: the shared read stays whole, the pages
/// leave the hidden items out with offsets and counts of what is left, also through the window of a folder past the
/// cap while another subject moves it, and a folder only leading on lists only the children leading on.
/// </summary>
[TestClass]
public sealed class RemoteListingFilterTests
{
    private static readonly StringComparer Names = StringComparer.OrdinalIgnoreCase;

    private readonly FakeProvider _provider = new();
    private readonly InstantClock _clock = new();
    private FakeAccount _account = null!;
    private IRemoteStorage _storage = null!;

    public TestContext TestContext { get; set; } = null!;

    private CancellationToken Token => TestContext.CancellationToken;

    [TestInitialize]
    public void Initialize()
    {
        _account = _provider.Account("alice");
        _storage = _provider.Open("alice", "", new());
    }

    [TestCleanup]
    public async Task CleanupAsync() => await _storage.DisposeAsync();

    [TestMethod]
    public async Task HiddenItemsAreLeftOutOfEveryPageAndOfTheCounts()
    {
        _account
            .WithFolder("Zoo")
            .WithFolder("Private")
            .WithFile("a.jpg", "a")
            .WithFile("b.txt", "b")
            .WithFile("secret.txt", "s")
            .WithFile("c.mp4", "c");
        var listings = new RemoteListings(_clock);
        var filter = Hiding("PRIVATE", "secret.txt");

        var (names, last) = await AllPagesAsync(listings, filter, 2);
        var everyone = await listings.ListAsync(_storage, Query(0, 10), Token);

        CollectionAssert.AreEqual(new[] { "Zoo", "a.jpg", "b.txt", "c.mp4" }, names);
        Assert.AreEqual((4, 1), (last.Total, last.DirectoryCount));
        Assert.AreEqual((6, 2), (everyone.Total, everyone.DirectoryCount), "the read itself is whole");
    }

    [TestMethod]
    public async Task AWindowPastTheCapKeepsTheOffsetsOfWhatIsLeftWhileAnotherSubjectMovesIt()
    {
        for (var n = 0; n < 12; n++)
            _account.WithFile($"f{n:00}.txt", "x");
        var listings = new RemoteListings(_clock, maxItems: 4);
        var filter = Hiding("f01.txt", "f02.txt", "f07.txt");

        var seen = new List<string>();
        var offset = 0;
        RemoteListing page;
        do
        {
            page = await listings.ListAsync(_storage, Query(offset, 2), filter, Token);
            seen.AddRange(page.Items.Select(i => i.Name));
            offset += page.Items.Count;
            // Someone else reads on through the same window, unfiltered.
            await listings.ListAsync(_storage, Query(offset + 3, 2), Token);
        } while (page.Items.Count > 0 && (page.HasMore || offset < page.Total));

        var expected = (await ProviderOrderAsync()).Except(["f01.txt", "f02.txt", "f07.txt"]);
        CollectionAssert.AreEqual(expected.ToArray(), seen.ToArray());
        Assert.AreEqual((9, false), (page.Total, page.HasMore));
    }

    [TestMethod]
    public async Task AFolderOnlyLeadingOnListsOnlyTheChildrenLeadingOn()
    {
        _account.WithFolder("Lead/Open").WithFolder("Lead/Closed").WithFile("Lead/x.txt", "x");
        var listings = new RemoteListings(_clock);
        var filter = new ListingFilter(new HashSet<string>(Names), new HashSet<string>(Names) { "open", "Gone" });

        var page = await listings.ListAsync(_storage, Query(0, 10) with { Path = "Lead" }, filter, Token);

        CollectionAssert.AreEqual(new[] { "Open" }, page.Items.Select(i => i.Name).ToArray());
        Assert.AreEqual((1, 1, false), (page.Total, page.DirectoryCount, page.HasMore));
        Assert.IsFalse(_account.Calls.Any(c => c.StartsWith("list", StringComparison.Ordinal)), "never listed");
    }

    private async Task<(List<string> Names, RemoteListing Last)> AllPagesAsync(
        RemoteListings listings,
        ListingFilter filter,
        int limit
    )
    {
        var names = new List<string>();
        RemoteListing page;
        do
        {
            page = await listings.ListAsync(_storage, Query(names.Count, limit), filter, Token);
            names.AddRange(page.Items.Select(i => i.Name));
        } while (page.Items.Count > 0 && names.Count < page.Total);
        return (names, page);
    }

    /// <summary>The root's names in the provider's own order.</summary>
    private async Task<List<string>> ProviderOrderAsync()
    {
        var names = new List<string>();
        string? cursor = null;
        do
        {
            var page = await _storage.ListAsync("", cursor, Token);
            names.AddRange(page.Items.Select(i => i.Name));
            cursor = page.Cursor;
        } while (cursor is not null);
        return names;
    }

    private static ListingFilter Hiding(params string[] names) => new(new HashSet<string>(names, Names));

    private static RemoteListingQuery Query(int offset, int limit) => new(1, "", offset, limit);
}
