using System.Diagnostics;
using Sfe.Core.Thumbnails;
using Sfe.Core.Trickplay;
using Sfe.Tests.Infrastructure;

namespace Sfe.Tests;

/// <summary>
/// Background trickplay generation with a real ffmpeg: set <c>SFE_TEST_FFMPEG</c> to its path, or put it on PATH.
/// Inconclusive without it.
/// </summary>
[TestClass]
public sealed class TrickplayTests
{
    private TestLibrary _library = null!;
    private CoreServices _services = null!;
    private CancellationTokenSource _stop = null!;
    private Task _worker = Task.CompletedTask;

    public TestContext TestContext { get; set; } = null!;

    private TrickplayService Trickplay => _services.Get<TrickplayService>();

    [TestInitialize]
    public async Task InitializeAsync()
    {
        _library = new TestLibrary();
        _services = await CoreServices.CreateAsync(_library, s =>
        {
            s["Sfe:Thumbnails:FfmpegPath"] = Environment.GetEnvironmentVariable("SFE_TEST_FFMPEG") is { Length: > 0 } path ? path : "ffmpeg";
            s["Sfe:Trickplay:Enabled"] = "true";
            s["Sfe:Trickplay:Folders:0"] = "Media";
            s["Sfe:Trickplay:IntervalSeconds"] = "1";
            s["Sfe:Trickplay:Width"] = "64";
            s["Sfe:Trickplay:Columns"] = "3";
            s["Sfe:Trickplay:Rows"] = "2";
        });
        if (!_services.Get<Ffmpeg>().IsAvailable)
            Assert.Inconclusive("ffmpeg not found: set SFE_TEST_FFMPEG to run the trickplay tests.");
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
    public async Task PassGeneratesTheVideosOfTheConfiguredFolders()
    {
        await RenderVideoAsync("Media/clip.mp4", seconds: 7.5);
        await RenderVideoAsync("Other/outside.mp4", seconds: 2);

        _worker = Trickplay.RunAsync(_stop.Token);
        var status = await WaitAsync(s => s.LastPassFinished is not null);

        var manifest = Trickplay.Get("Media/clip.mp4");
        Assert.IsNotNull(manifest);
        Assert.AreEqual(8, manifest.Count);         // seconds 0 to 7
        Assert.AreEqual(2, manifest.Sheets);        // 6 frames per sheet
        Assert.AreEqual(64, manifest.TileWidth);
        Assert.AreEqual(36, manifest.TileHeight);
        Assert.IsNotNull(Trickplay.GetSheet("Media/clip.mp4", 1));
        Assert.IsNull(Trickplay.GetSheet("Media/clip.mp4", 2));

        // The fixture's other "videos" are not real ones: recorded as failed, not tried again by the next pass.
        Assert.AreEqual(1, status.Generated);
        Assert.AreEqual(3, status.Failed);
        Assert.IsNull(Trickplay.Get("Media/Episode 2.mkv"));

        // Outside the configured folders: neither generated nor queued when asked for.
        Assert.IsNull(Trickplay.Get("Other/outside.mp4"));
        Assert.IsFalse(Directory.Exists(Trickplay.GetCacheFolder(_library.FullPath("Other/outside.mp4"))));
        Assert.AreEqual(0, Trickplay.Status.Queued);

        // The activity lists what was done, nothing is left waiting.
        var activity = Trickplay.GetActivity(10);
        Assert.IsFalse(activity.Status.Discovering);
        Assert.AreEqual(0, activity.Status.Pending);
        Assert.IsEmpty(activity.Pending);
        Assert.IsEmpty(activity.Running);
        Assert.HasCount(4, activity.Recent);
        Assert.IsTrue(activity.Recent.Single(r => r.Path == "Media/clip.mp4").Succeeded);
        Assert.IsTrue(activity.Recent.Where(r => !r.Succeeded).All(r => !string.IsNullOrEmpty(r.Error)));
        Assert.IsEmpty(Trickplay.GetStates(["Media/clip.mp4", "Media/Episode 2.mkv"]));
    }

    [TestMethod]
    public async Task RequestedVideoShowsAsWaitingUntilDone()
    {
        await RenderVideoAsync("Media/later.mp4", seconds: 3);

        // A thumbnail's hover preview only looks.
        Assert.IsNull(Trickplay.Get("Media/later.mp4", queue: false));
        Assert.IsEmpty(Trickplay.GetStates(["Media/later.mp4"]));

        // Queued by the player's request; the worker isn't started yet, so it stays waiting.
        Assert.IsNull(Trickplay.Get("Media/later.mp4"));
        var states = Trickplay.GetStates(["Media/later.mp4", "Media/missing.mp4", "../outside.mp4"]);
        Assert.HasCount(1, states);
        Assert.AreEqual(TrickplayFileState.Pending, states["Media/later.mp4"]);
        CollectionAssert.AreEqual(new[] { "Media/later.mp4" }, Trickplay.GetActivity(10).Queued.ToArray());

        _worker = Trickplay.RunAsync(_stop.Token);
        await WaitAsync(_ => Trickplay.GetStates(["Media/later.mp4"]).Count == 0);
        Assert.IsNotNull(Trickplay.Get("Media/later.mp4"));
        Assert.IsTrue(Trickplay.GetActivity(10).Recent.Single(r => r.Path == "Media/later.mp4").Succeeded);
    }

    [TestMethod]
    public async Task ChangedVideoIsQueuedWhenRequestedAndKeepsNoStaleTrickplay()
    {
        _worker = Trickplay.RunAsync(_stop.Token);
        await WaitAsync(s => s.LastPassFinished is not null);
        Assert.IsNull(Trickplay.Get("Media/later.mp4"));

        // Added after the pass: the player's request queues it right away.
        await RenderVideoAsync("Media/later.mp4", seconds: 3);
        Assert.IsNull(Trickplay.Get("Media/later.mp4"));
        await WaitAsync(_ => Trickplay.Get("Media/later.mp4") is not null);
        Assert.AreEqual(3, Trickplay.Get("Media/later.mp4")!.Count);

        // Replaced by another video: the old frames are not served while the new ones are made.
        await RenderVideoAsync("Media/later.mp4", seconds: 5);
        File.SetLastWriteTimeUtc(_library.FullPath("Media/later.mp4"), DateTime.UtcNow.AddMinutes(1));
        var refreshed = Trickplay.Get("Media/later.mp4");
        Assert.IsTrue(refreshed is null || refreshed.Count == 5);
        await WaitAsync(_ => Trickplay.Get("Media/later.mp4")?.Count == 5);
    }

    private async Task<TrickplayStatus> WaitAsync(Func<TrickplayStatus, bool> condition)
    {
        var watch = Stopwatch.StartNew();
        while (true)
        {
            var status = Trickplay.Status;
            if (condition(status))
                return status;
            if (watch.Elapsed > TimeSpan.FromSeconds(60))
                Assert.Fail($"Timed out waiting for trickplay: {status}");
            await Task.Delay(100, TestContext.CancellationToken);
        }
    }

    /// <summary>A small test pattern video with a key frame every second.</summary>
    private async Task RenderVideoAsync(string relativePath, double seconds)
    {
        var path = _library.FullPath(relativePath);
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        var (exitCode, errors) = await _services.Get<Ffmpeg>().RunAsync(
            ["-hide_banner", "-loglevel", "error", "-nostdin", "-y",
             "-f", "lavfi", "-i", $"testsrc=duration={seconds}:size=320x180:rate=10",
             "-g", "10", "-pix_fmt", "yuv420p", path],
            TimeSpan.FromSeconds(30), cancellationToken: TestContext.CancellationToken);
        Assert.AreEqual(0, exitCode, errors);
    }
}
