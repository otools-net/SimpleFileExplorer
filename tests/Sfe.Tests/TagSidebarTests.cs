using Otools.Net.Sfe.Core.Options;
using Otools.Net.Sfe.Core.Tagging;
using Otools.Net.Sfe.Tests.Infrastructure;

namespace Otools.Net.Sfe.Tests;

/// <summary>
/// The sidebar's lists that settings switch off ("Most popular", "Last searched", "I feel lucky"): off by default,
/// empty when off, and not read then.
/// </summary>
[TestClass]
public sealed class TagSidebarTests
{
    private TestLibrary _library = null!;
    private CoreServices _services = null!;
    private readonly CommandLog _log = new();

    /// <summary>Every list of the sidebar shown (some are off by default).</summary>
    internal static readonly TagSidebarChoice EveryList = new(
        5,
        History: true,
        Popular: true,
        Searched: true,
        Lucky: true
    );

    public TestContext TestContext { get; set; } = null!;

    private TagService Tags => _services.Get<TagService>();

    [TestInitialize]
    public async Task InitializeAsync()
    {
        _library = new TestLibrary();
        _services = await CoreServices.CreateAsync(_library, logging: _log.AddTo);
    }

    [TestCleanup]
    public async Task CleanupAsync()
    {
        await _services.DisposeAsync();
        _library.Dispose();
    }

    [TestMethod]
    public async Task ByDefaultOnlyTheLastViewedTagsAreShown()
    {
        await TagSearchAndViewAsync();

        var sidebar = await Tags.GetSidebarAsync(cancellationToken: TestContext.CancellationToken);

        Assert.IsFalse(new TagOptions().SidebarPopular || new TagOptions().SidebarSearched);
        Assert.IsFalse(new TagOptions().SidebarLucky);
        Assert.IsEmpty(sidebar.Popular);
        Assert.IsEmpty(sidebar.Searched);
        Assert.IsEmpty(sidebar.Lucky);
        Assert.AreEqual("Favorite", sidebar.Viewed.Single().Tag.Name);
        Assert.IsFalse(sidebar.ShowPopular || sidebar.ShowSearched || sidebar.ShowLucky);
        Assert.IsTrue(sidebar.HistoryEnabled);
    }

    [TestMethod]
    public async Task ListsSwitchedOffAreNotRead()
    {
        await TagSearchAndViewAsync();
        _log.Clear();

        var sidebar = await Tags.GetSidebarAsync(
            new TagSidebarChoice(5, History: true, Popular: false, Searched: false, Lucky: false),
            TestContext.CancellationToken
        );

        Assert.HasCount(1, sidebar.Viewed);
        Assert.IsFalse(
            _log.Commands.Any(command => command.Contains("LastSearchedAt", StringComparison.Ordinal)),
            "The last searched tags aren't queried: " + string.Join("\n\n", _log.Commands)
        );
    }

    [TestMethod]
    public async Task ListsSwitchedOnAreFilled()
    {
        var other = await CreateTagAsync("Other");
        await TagSearchAndViewAsync();

        var sidebar = await Tags.GetSidebarAsync(
            new TagSidebarChoice(5, History: true, Popular: true, Searched: true, Lucky: true),
            TestContext.CancellationToken
        );

        Assert.AreEqual("Favorite", sidebar.Popular.Single().Tag.Name);
        Assert.AreEqual("Favorite", sidebar.Searched.Single().Tag.Name);
        Assert.AreEqual("Favorite", sidebar.Viewed.Single().Tag.Name);
        Assert.Contains(other, sidebar.Lucky.Select(u => u.Tag.Id).ToList());
        Assert.IsTrue(sidebar.ShowPopular && sidebar.ShowSearched && sidebar.ShowLucky);
    }

    [TestMethod]
    public async Task TheLastSearchedTagsNeedTheHistory()
    {
        await TagSearchAndViewAsync();

        var sidebar = await Tags.GetSidebarAsync(
            new TagSidebarChoice(5, History: false, Popular: false, Searched: true, Lucky: false),
            TestContext.CancellationToken
        );

        Assert.IsEmpty(sidebar.Searched);
        Assert.IsEmpty(sidebar.Viewed);
        Assert.IsTrue(sidebar.ShowSearched);
        Assert.IsFalse(sidebar.HistoryEnabled);
    }

    /// <summary>Tags a file Favorite, then records a search naming that tag and a view of the file.</summary>
    private async Task TagSearchAndViewAsync()
    {
        var (tagged, _) = await Tags.ChangeFileTagsAsync(
            new FileTagChange(["RFile1.txt"], Add: [BuiltInTags.Favorite]),
            TestContext.CancellationToken
        );
        Assert.IsNotNull(tagged);
        await Tags.RecordSearchedAsync([BuiltInTags.Favorite], TestContext.CancellationToken);
        Assert.IsTrue(await Tags.RecordViewedAsync("RFile1.txt", TestContext.CancellationToken));
    }

    private async Task<int> CreateTagAsync(string name)
    {
        var result = await Tags.CreateAsync(new TagInput(name), TestContext.CancellationToken);
        Assert.IsNotNull(result.Tag);
        return result.Tag.Id;
    }
}
