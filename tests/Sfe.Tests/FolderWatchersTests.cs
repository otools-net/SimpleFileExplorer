using Microsoft.Extensions.Logging.Abstractions;
using Otools.Net.Sfe.Core.Library;
using Otools.Net.Sfe.Core.Library.Watching;
using Otools.Net.Sfe.Core.Paths;
using Otools.Net.Sfe.Tests.Infrastructure;

namespace Otools.Net.Sfe.Tests;

/// <summary>
/// Folders followed for changes made outside SFE (<see cref="FolderWatchers"/>): by the system's watcher and by
/// polling, only while held, SFE's own changes and items being written left out. Timings shortened.
/// </summary>
[TestClass]
public sealed class FolderWatchersTests
{
    private static readonly FolderWatchTimings Quick = new(
        TimeSpan.FromMilliseconds(50),
        TimeSpan.FromMilliseconds(100),
        TimeSpan.FromMilliseconds(200),
        TimeSpan.FromSeconds(1)
    );

    /// <summary>Long enough for a change to be told when it would be (settle, gap, a poll), with room.</summary>
    private static readonly TimeSpan Quiet = TimeSpan.FromMilliseconds(800);

    private TestLibrary _library = null!;
    private CoreServices _services = null!;
    private readonly List<FolderWatchers> _watchers = [];

    public TestContext TestContext { get; set; } = null!;

    private CancellationToken Token => TestContext.CancellationToken;

    [TestInitialize]
    public async Task InitializeAsync()
    {
        _library = new TestLibrary();
        _services = await CoreServices.CreateAsync(_library);
    }

    [TestCleanup]
    public async Task CleanupAsync()
    {
        _watchers.ForEach(w => w.Dispose());
        await _services.DisposeAsync();
        _library.Dispose();
    }

    [TestMethod]
    [DataRow(false, DisplayName = "watched")]
    [DataRow(true, DisplayName = "polled")]
    public async Task AFileAddedOutsideSfeIsToldForTheFolderHeld(bool polled)
    {
        var watchers = Create(polled);
        var told = Listen(watchers);
        using var watch = watchers.Watch("ChildDir");

        await WriteUntilToldAsync(told, "ChildDir");

        Assert.IsTrue(watch.IsActive);
        Assert.AreEqual(polled, watchers.IsPolled("ChildDir"));
        CollectionAssert.AreEqual(new[] { "ChildDir" }, told.ToArray().Distinct().ToList());
    }

    [TestMethod]
    public async Task AFolderIsFollowedOnceWhileSomeoneHoldsIt()
    {
        var watchers = Create(polled: true);
        var first = watchers.Watch("ChildDir");
        var second = watchers.Watch("ChildDir");
        using var missing = watchers.Watch("NoSuchFolder");
        using var outside = watchers.Watch("../elsewhere");

        CollectionAssert.AreEqual(new[] { "ChildDir" }, watchers.Folders.ToList());
        first.Dispose();
        first.Dispose(); // twice: still held by the second
        CollectionAssert.AreEqual(new[] { "ChildDir" }, watchers.Folders.ToList());
        second.Dispose();

        Assert.IsEmpty(watchers.Folders);
        Assert.IsFalse(missing.IsActive);
        Assert.IsFalse(outside.IsActive);

        // Let go: a change there is no longer told.
        var told = Listen(watchers);
        _library.AddFile("ChildDir/after.txt", 10);
        await Task.Delay(Quiet, Token);
        Assert.IsEmpty(told);
    }

    [TestMethod]
    [DataRow(false, DisplayName = "watched")]
    [DataRow(true, DisplayName = "polled")]
    public async Task SfesOwnChangeIsNotToldAgain(bool polled)
    {
        var watchers = Create(polled);
        var told = Listen(watchers);
        using var watch = watchers.Watch("ChildDir");
        await WriteUntilToldAsync(told, "ChildDir");
        await Task.Delay(Quiet, Token);
        told.Clear();

        // As SFE does: the file written, then the folder's listings forgotten (which tells the change feed).
        _library.AddFile("ChildDir/by-sfe.txt", 10);
        _services.Get<LibraryBrowser>().Invalidate(_library.FullPath("ChildDir"));
        await Task.Delay(Quiet, Token);

        Assert.IsEmpty(told);
    }

    [TestMethod]
    public async Task ItemsBeingWrittenAreNotTold()
    {
        var watchers = Create(polled: false);
        var told = Listen(watchers);
        using var watch = watchers.Watch("ChildDir");
        await WriteUntilToldAsync(told, "ChildDir");
        await Task.Delay(Quiet, Token);
        told.Clear();

        await File.WriteAllTextAsync(_library.FullPath("ChildDir/copy.mp4" + LibraryPaths.PartialSuffix), "x", Token);
        await Task.Delay(Quiet, Token);

        Assert.IsEmpty(told);
    }

    [TestMethod]
    public void TheWorkFolderIsLeftOutAtTheTopOnly()
    {
        Assert.IsTrue(FolderWatchers.IsIgnored(LibraryPaths.WorkFolderName, top: true));
        Assert.IsFalse(FolderWatchers.IsIgnored(LibraryPaths.WorkFolderName, top: false));
        Assert.IsTrue(FolderWatchers.IsIgnored("clip.mp4" + LibraryPaths.PartialSuffix, top: false));
        Assert.IsFalse(FolderWatchers.IsIgnored("clip.mp4", top: true));
    }

    private FolderWatchers Create(bool polled)
    {
        var watchers = new FolderWatchers(
            _services.Get<LibraryPaths>(),
            _services.Get<LibraryBrowser>(),
            NullLogger<FolderWatchers>.Instance
        )
        {
            Timings = Quick,
            PollOnly = polled,
        };
        _watchers.Add(watchers);
        return watchers;
    }

    private static System.Collections.Concurrent.ConcurrentQueue<string> Listen(FolderWatchers watchers)
    {
        var told = new System.Collections.Concurrent.ConcurrentQueue<string>();
        watchers.Changed += told.Enqueue;
        return told;
    }

    /// <summary>
    /// Writes a new file in <paramref name="folder"/> every 300 ms until a change of it is told (the watcher or the
    /// first poll starts on another thread: a file written before it is not a change).
    /// </summary>
    private async Task WriteUntilToldAsync(System.Collections.Concurrent.ConcurrentQueue<string> told, string folder)
    {
        for (var i = 0; i < 30; i++)
        {
            _library.AddFile($"{folder}/outside-{i}.txt", 10 + i);
            await Task.Delay(300, Token);
            if (told.Contains(folder))
                return;
        }
        Assert.Fail($"No change of {folder} was told.");
    }
}
