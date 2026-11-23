using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Otools.Net.Sfe.Core.Data;
using Otools.Net.Sfe.Core.Indexing;
using Otools.Net.Sfe.Core.Library;
using Otools.Net.Sfe.Core.Options;
using Otools.Net.Sfe.Core.Paths;
using Otools.Net.Sfe.Tests.Infrastructure;

namespace Otools.Net.Sfe.Tests;

/// <summary>
/// Scan as you go (<see cref="ListingIndexer"/>): a folder listed is indexed in the background, that folder only, at
/// most once per interval, never under a scan or a file operation, and its listings are told when the index changed.
/// </summary>
[TestClass]
public sealed class ListingIndexerTests
{
    private static readonly TimeSpan Wait = TimeSpan.FromSeconds(20);

    private TestLibrary _library = null!;
    private CoreServices _services = null!;
    private readonly List<ListingIndexer> _indexers = [];
    private readonly Clock _clock = new();

    public TestContext TestContext { get; set; } = null!;

    private CancellationToken Token => TestContext.CancellationToken;

    [TestInitialize]
    public void Initialize() => _library = new TestLibrary();

    [TestCleanup]
    public async Task CleanupAsync()
    {
        _indexers.ForEach(i => i.Dispose());
        if (_services is not null)
            await _services.DisposeAsync();
        _library.Dispose();
    }

    [TestMethod]
    public async Task AListedFolderIsIndexedWithoutTheFoldersInsideIt()
    {
        var indexer = await CreateAsync();
        var told = new List<string>();
        _services.Get<LibraryBrowser>().FolderChanged += told.Add;

        Assert.IsTrue(await ListAsync(indexer, "RepeatedDir"));

        CollectionAssert.AreEquivalent(
            new[] { "RDFile1.txt", "RDFile2.txt" },
            await IndexedNamesAsync("RepeatedDir")
        );
        Assert.IsEmpty(await IndexedNamesAsync("RepeatedDir/ChildDir"));
        CollectionAssert.AreEqual(new[] { "RepeatedDir" }, told);
    }

    [TestMethod]
    public async Task AFolderIsIndexedAgainOnlyAfterTheInterval()
    {
        var indexer = await CreateAsync();
        Assert.IsTrue(await ListAsync(indexer, "ChildDir"));
        _library.AddFile("ChildDir/new.txt", 10);

        // Within the interval: not queued at all.
        indexer.Listed("ChildDir");
        Assert.IsNull(await NextDoneAsync(indexer, TimeSpan.FromMilliseconds(500)));
        CollectionAssert.DoesNotContain(await IndexedNamesAsync("ChildDir"), "new.txt");

        _clock.Advance(ListingIndexer.Interval);
        Assert.IsTrue(await ListAsync(indexer, "ChildDir"));
        CollectionAssert.Contains(await IndexedNamesAsync("ChildDir"), "new.txt");

        // Nothing changed since: the listings aren't told.
        _clock.Advance(ListingIndexer.Interval);
        Assert.IsFalse(await ListAsync(indexer, "ChildDir"));
    }

    [TestMethod]
    public async Task AFileGoneFromTheFolderLeavesTheIndex()
    {
        var indexer = await CreateAsync();
        await ListAsync(indexer, "ChildDir");
        File.Delete(_library.FullPath("ChildDir/AFile2"));

        _clock.Advance(ListingIndexer.Interval);
        Assert.IsTrue(await ListAsync(indexer, "ChildDir"));

        CollectionAssert.AreEqual(new[] { "AFile1.txt" }, await IndexedNamesAsync("ChildDir"));
    }

    [TestMethod]
    public async Task AFolderUnderAFileOperationIsLeftAndAskedAgainLater()
    {
        var indexer = await CreateAsync();
        var scans = _services.Get<ScanCoordinator>();
        using (scans.TryBeginOperation(_library.FullPath("ChildDir/AFile1.txt")))
            Assert.IsFalse(await ListAsync(indexer, "ChildDir"));
        Assert.IsEmpty(await IndexedNamesAsync("ChildDir"));

        // Left, not counted as done: the next listing asks again at once.
        Assert.IsTrue(await ListAsync(indexer, "ChildDir"));
        Assert.HasCount(2, await IndexedNamesAsync("ChildDir"));
    }

    [TestMethod]
    public async Task AScanWaitsForAFolderBeingIndexed()
    {
        await CreateAsync();
        var scans = _services.Get<ScanCoordinator>();
        using (var held = scans.TryBeginFolderIndex(_library.FullPath("ChildDir")))
        {
            Assert.IsNotNull(held);
            // The same folder, or one above it, can't be taken twice.
            Assert.IsNull(scans.TryBeginFolderIndex(_library.Root));
            Assert.IsTrue(scans.TryStart("ChildDir"));
            await Task.Delay(300, Token);
            Assert.AreEqual(ScanState.Running, scans.Status.State);
            Assert.IsNull(scans.TryBeginFolderIndex(_library.FullPath("ChildDir")));
        }
        await scans.Completion!.WaitAsync(Wait, Token);
        Assert.AreEqual(ScanState.Completed, scans.Status.State);
    }

    [TestMethod]
    public async Task NothingIsIndexedWithTheSettingOffNorInAnExcludedFolder()
    {
        var indexer = await CreateAsync(settings =>
        {
            settings["Sfe:Scan:ExcludedFolders:0"] = "Media";
        });
        Assert.IsFalse(await ListAsync(indexer, "Media"));
        Assert.IsEmpty(await IndexedNamesAsync("Media"));

        await _services.DisposeAsync();
        _services = null!;
        var off = await CreateAsync(settings => settings["Sfe:Scan:IndexWhenListed"] = "false");
        off.Listed("ChildDir");
        off.Listed("../outside");
        Assert.IsNull(await NextDoneAsync(off, TimeSpan.FromMilliseconds(500)));
        Assert.IsEmpty(await IndexedNamesAsync("ChildDir"));
    }

    private async Task<ListingIndexer> CreateAsync(Action<IDictionary<string, string?>>? configure = null)
    {
        _services = await CoreServices.CreateAsync(
            _library,
            settings =>
            {
                settings["Sfe:Scan:IndexWhenListed"] = "true";
                configure?.Invoke(settings);
            }
        );
        var indexer = new ListingIndexer(
            _services.Get<DirectoryIndexer>(),
            _services.Get<ScanCoordinator>(),
            _services.Get<LibraryBrowser>(),
            _services.Get<LibraryPaths>(),
            _services.Get<IOptionsMonitor<SfeOptions>>(),
            NullLogger<ListingIndexer>.Instance
        )
        {
            Clock = _clock,
        };
        _indexers.Add(indexer);
        return indexer;
    }

    /// <summary>Lists <paramref name="folder"/> and waits for it to be dealt with: whether its index changed.</summary>
    private async Task<bool> ListAsync(ListingIndexer indexer, string folder)
    {
        var done = NextDoneAsync(indexer, Wait);
        indexer.Listed(folder);
        return await done ?? throw new AssertFailedException($"{folder} was not dealt with");
    }

    /// <summary>
    /// The outcome of the next folder dealt with, or null when none is within <paramref name="within"/>.
    /// </summary>
    private async Task<bool?> NextDoneAsync(ListingIndexer indexer, TimeSpan within)
    {
        var done = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        void OnDone(string _, bool changed) => done.TrySetResult(changed);
        indexer.Done += OnDone;
        try
        {
            return await done.Task.WaitAsync(within, Token);
        }
        catch (TimeoutException)
        {
            return null;
        }
        finally
        {
            indexer.Done -= OnDone;
        }
    }

    private async Task<List<string>> IndexedNamesAsync(string folder)
    {
        var full = _library.FullPath(folder);
        await using var db = await _services
            .Get<IDbContextFactory<SfeDbContext>>()
            .CreateDbContextAsync(Token);
        var files = await db
            .Files.Where(f => f.Directory!.FullName == full)
            .Select(f => f.FullName)
            .ToListAsync(Token);
        return [.. files.Select(Path.GetFileName).OfType<string>()];
    }

    private sealed class Clock : TimeProvider
    {
        private DateTimeOffset _now = DateTimeOffset.UtcNow;

        public void Advance(TimeSpan by) => _now += by;

        public override DateTimeOffset GetUtcNow() => _now;
    }
}
