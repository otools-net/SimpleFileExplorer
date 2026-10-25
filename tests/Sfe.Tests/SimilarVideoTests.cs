using System.Diagnostics;
using Sfe.Core.FileTracking;
using Sfe.Core.Indexing;
using Sfe.Core.Thumbnails;
using Sfe.Core.Trickplay;
using Sfe.Tests.Infrastructure;

namespace Sfe.Tests;

/// <summary>
/// Similar videos from end to end (trickplay, fingerprint, index, comparison) with a real ffmpeg: set
/// <c>SFE_TEST_FFMPEG</c> to its path, or put it on PATH. Inconclusive without it.
/// </summary>
[TestClass]
public sealed class SimilarVideoTests
{
    private TestLibrary _library = null!;
    private CoreServices _services = null!;
    private CancellationTokenSource _stop = null!;
    private Task _worker = Task.CompletedTask;

    public TestContext TestContext { get; set; } = null!;

    private TrickplayService Trickplay => _services.Get<TrickplayService>();
    private SimilarVideoFinder Finder => _services.Get<SimilarVideoFinder>();

    [TestInitialize]
    public async Task InitializeAsync()
    {
        _library = new TestLibrary();
        _services = await CoreServices.CreateAsync(_library, s =>
        {
            s["Sfe:Thumbnails:FfmpegPath"] = Environment.GetEnvironmentVariable("SFE_TEST_FFMPEG") is { Length: > 0 } path ? path : "ffmpeg";
            s["Sfe:Trickplay:Enabled"] = "true";
            s["Sfe:Trickplay:Folders:0"] = "Clips";
            s["Sfe:Trickplay:IntervalSeconds"] = "1";
            s["Sfe:Trickplay:Width"] = "64";
            s["Sfe:Trickplay:Columns"] = "3";
            s["Sfe:Trickplay:Rows"] = "2";
        });
        if (!_services.Get<Ffmpeg>().IsAvailable)
            Assert.Inconclusive("ffmpeg not found: set SFE_TEST_FFMPEG to run the similar video tests.");
        _stop = CancellationTokenSource.CreateLinkedTokenSource(TestContext.CancellationToken);
    }

    [TestCleanup]
    public async Task CleanupAsync()
    {
        if (_stop is not null)
        {
            await _stop.CancelAsync();
            await _worker;
            _stop.Dispose();
        }

        await _services.DisposeAsync();
        _library.Dispose();
    }

    [TestMethod]
    public async Task SmallerCopyOfAVideoIsFoundWithItsMatch()
    {
        // 20 seconds of a moving test picture, a key frame every second; the small one is a copy of the first.
        TestSamples.CopyTo("mandelbrot-20s.mp4", _library.FullPath("Clips/zoom.mp4"));
        TestSamples.CopyTo("life-20s.mp4", _library.FullPath("Clips/life.mp4"));
        TestSamples.CopyTo("mandelbrot-20s-small.mp4", _library.FullPath("Clips/Copies/zoom small.mp4"));

        var scan = _services.Get<ScanCoordinator>();
        Assert.IsTrue(scan.TryStart(""));
        await scan.Completion!;
        _worker = Trickplay.RunAsync(_stop.Token);
        await WaitAsync(s => s.LastPassFinished is not null);

        var before = await Finder.FindAsync("", null, null, TestContext.CancellationToken);
        Assert.AreEqual(SimilarVideoState.Idle, before!.Status.State);
        Assert.IsNull(before.Summary);

        Assert.IsTrue(Finder.TryStart());
        await Finder.Completion;
        var results = await Finder.FindAsync("", null, null, TestContext.CancellationToken);
        Assert.AreEqual(SimilarVideoState.Completed, results!.Status.State, results.Status.Message);
        // The fixture's other videos are fake ones, without trickplay.
        Assert.AreEqual(3, results.Summary!.Compared);
        Assert.AreEqual(6, results.Summary.Videos);

        var group = results.Groups.Single();
        Assert.AreEqual("Clips/zoom.mp4", group.Videos[0].File.Path);      // same length, bigger file
        Assert.IsNull(group.Videos[0].Match);
        Assert.AreEqual(20, group.Videos[0].Duration);
        Assert.AreEqual("Clips/Copies/zoom small.mp4", group.Videos[1].File.Path);
        Assert.IsGreaterThanOrEqualTo(90, group.Videos[1].Match!.Value);

        // A folder lists the groups with a video in it.
        Assert.HasCount(1, (await Finder.FindAsync("Clips/Copies", null, null, TestContext.CancellationToken))!.Groups);
        Assert.IsEmpty((await Finder.FindAsync("Media", null, null, TestContext.CancellationToken))!.Groups);
        // Pages of another comparison are refused.
        Assert.IsNull(await Finder.FindAsync("", "0:0", null, TestContext.CancellationToken));
    }

    private Task<TrickplayStatus> WaitAsync(Func<TrickplayStatus, bool> condition) =>
        SlowWork.UntilAsync(() => Trickplay.Status, condition, 60, "trickplay", TestContext.CancellationToken);
}
