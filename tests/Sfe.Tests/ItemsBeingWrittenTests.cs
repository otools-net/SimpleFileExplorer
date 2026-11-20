using Otools.Net.Sfe.Core.Files;
using Otools.Net.Sfe.Core.Paths;
using Otools.Net.Sfe.Tests.Infrastructure;

namespace Otools.Net.Sfe.Tests;

/// <summary>
/// The items being written and how far each is (<see cref="ItemsBeingWritten"/>): what a folder holds, when its
/// listings are told (ticks called by hand, the timer's own beat is not waited for).
/// </summary>
[TestClass]
public sealed class ItemsBeingWrittenTests
{
    private TestLibrary _library = null!;
    private ItemsBeingWritten _writing = null!;
    private readonly List<string> _told = [];

    [TestInitialize]
    public void Initialize()
    {
        _library = new TestLibrary();
        _writing = new ItemsBeingWritten(new LibraryPaths(_library.Root)) { Manual = true };
        _writing.FolderProgressed += _told.Add;
    }

    [TestCleanup]
    public void Cleanup()
    {
        _writing.Dispose();
        _library.Dispose();
    }

    [TestMethod]
    public void AFolderListsItsItemsBeingWrittenWithTheirProgress()
    {
        using var write = _writing.Begin(_library.FullPath("Media/clip.mp4.sfe-partial"), "clip.mp4", 1000);
        using var other = _writing.Begin(_library.FullPath("Docs.sfe-partial"), "Docs", 0);
        write.Add(250);
        write.Add(150);

        var item = _writing.In("Media").Single();

        Assert.AreEqual(new ItemWriting("clip.mp4.sfe-partial", "clip.mp4", 400, 1000), item);
        Assert.AreEqual("Docs", _writing.In("").Single().FinalName);
        Assert.IsEmpty(_writing.In("ChildDir"));
    }

    [TestMethod]
    public void AFolderIsToldOncePerTickWhenItsItemsMoveAndWhenTheyEnd()
    {
        var first = _writing.Begin(_library.FullPath("Media/a.sfe-partial"), "a", 10);
        using var second = _writing.Begin(_library.FullPath("Media/b.sfe-partial"), "b", 10);

        _writing.Tick(); // both new
        first.Add(1);
        second.Add(1);
        _writing.Tick(); // both moved: one event
        _writing.Tick(); // nothing moved
        first.Dispose();
        _writing.Tick(); // one ended

        CollectionAssert.AreEqual(new[] { "Media", "Media", "Media" }, _told);
        Assert.AreEqual("b", _writing.In("Media").Single().FinalName);
    }

    [TestMethod]
    public void AStalledWriteIsToldAgainNowAndThen()
    {
        using var write = _writing.Begin(_library.FullPath("Media/a.sfe-partial"), "a", 10);

        for (var tick = 0; tick < 20; tick++)
            _writing.Tick();

        // Once as new, then at each heartbeat (every tenth tick).
        Assert.HasCount(3, _told);
    }

    [TestMethod]
    public void AWriteOutsideTheLibraryIsNotTold()
    {
        using var write = _writing.Begin(Path.Combine(Path.GetTempPath(), "elsewhere.sfe-partial"), "elsewhere", 10);
        write.Add(5);

        _writing.Tick();

        Assert.IsEmpty(_told);
    }
}
