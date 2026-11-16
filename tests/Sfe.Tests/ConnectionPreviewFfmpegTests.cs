using System.Net;
using System.Net.Http.Json;
using Otools.Net.Sfe.Api.Endpoints;
using Otools.Net.Sfe.Core.Access;
using Otools.Net.Sfe.Core.Thumbnails;
using Otools.Net.Sfe.Tests.Infrastructure;

namespace Otools.Net.Sfe.Tests;

/// <summary>
/// What ffmpeg makes of connections' files through the content cache: a picture the provider doesn't thumbnail, a
/// video converted while it plays, their sizes and lengths on the library's rows; never a script named as media. Need
/// ffmpeg: set <c>SFE_TEST_FFMPEG</c> to its path, or put it on PATH; inconclusive without it.
/// </summary>
[TestClass]
public sealed class ConnectionPreviewFfmpegTests
{
    private RuledConnection _world = null!;

    public TestContext TestContext { get; set; } = null!;

    private CancellationToken Token => TestContext.CancellationToken;

    [TestInitialize]
    public async Task InitializeAsync()
    {
        _world = await RuledConnection.CreateAsync(Token, settings =>
            settings["Sfe:Thumbnails:FfmpegPath"] = Environment.GetEnvironmentVariable("SFE_TEST_FFMPEG")
                is { Length: > 0 } path
                ? path
                : "ffmpeg"
        );
        if (!(await _world.LibraryAsync<Ffmpeg>(Token)).IsAvailable)
        {
            await _world.DisposeAsync();
            Assert.Inconclusive("ffmpeg not found: set SFE_TEST_FFMPEG to run the previews made by ffmpeg.");
        }
        // Videos are converted for those with files.stream (Writers don't have it).
        var watchers = await _world.GroupAsync("Watchers", [Rights.UseConnections, Rights.Stream], Token);
        await _world.ShareAsync(new() { [_world.WritersId] = FolderLevel.Write, [watchers] = FolderLevel.Read }, Token);
        _world
            .Account.WithBytes("Public/photo.png", File.ReadAllBytes(TestSamples.PathOf("testsrc-64x48-still.png")))
            .WithBytes("Public/clip.mp4", File.ReadAllBytes(TestSamples.PathOf("testsrc-1s-320x180.mp4")))
            .WithFile("Public/list.png", "#EXTM3U\nhttp://127.0.0.1:9/a.ts\n")
            .WithFile("Public/film.mp4", "ffconcat version 1.0\nfile 'clip.mp4'\n");
        await _world.IndexAsync(Token);
        _world.Account.Calls.Clear();
    }

    [TestCleanup]
    public async Task CleanupAsync() => await _world.DisposeAsync();

    [TestMethod]
    public async Task APictureTheProviderDoesNotThumbnailIsMadeFromTheCacheAndItsSizeListed()
    {
        using var thumbnail = await _world.Bob.GetAsync($"{_world.Base}/thumbnails/Public/photo.png", Token);
        var bytes = await thumbnail.Content.ReadAsByteArrayAsync(Token);
        var listing = await ListingAsync();

        Assert.AreEqual(HttpStatusCode.OK, thumbnail.StatusCode);
        CollectionAssert.AreEqual(new byte[] { 0xFF, 0xD8 }, bytes.Take(2).ToArray(), "A JPEG made by ffmpeg");
        var photo = listing.Listing.Items.Single(i => i.Name == "photo.png");
        Assert.AreEqual((64, 48), (photo.Width, photo.Height));
    }

    [TestMethod]
    public async Task AVideoPlaysConvertedFromTheCacheAndItsLengthIsListed()
    {
        var probe = await _world.Bob.GetFromJsonAsync<StreamProbe>(
            $"{_world.Base}/streaming/probe?path=Public/clip.mp4",
            RuledConnection.Json,
            Token
        );
        using var play = await _world.Bob.GetAsync(
            $"{_world.Base}/streaming/play?path=Public/clip.mp4&t=0&player=p1",
            HttpCompletionOption.ResponseHeadersRead,
            Token
        );
        var listing = await ListingAsync();

        Assert.AreEqual(1, probe!.Duration!.Value, 0.2);
        Assert.AreEqual(HttpStatusCode.OK, play.StatusCode);
        Assert.AreEqual("video/mp4", play.Content.Headers.ContentType?.MediaType);
        Assert.AreEqual(1, _world.Account.Calls.Count(c => c == "read Public/clip.mp4"), "Downloaded once");
        var clip = listing.Listing.Items.Single(i => i.Name == "clip.mp4");
        Assert.AreEqual(1, clip.Duration!.Value, 0.2);
        Assert.AreEqual((320, 180), (clip.Width, clip.Height));
    }

    [TestMethod]
    public async Task AScriptNamedAsMediaIsNeitherThumbnailedNorPlayedAndNotFetchedTwice()
    {
        using var picture = await _world.Bob.GetAsync($"{_world.Base}/thumbnails/Public/list.png", Token);
        using var again = await _world.Bob.GetAsync($"{_world.Base}/thumbnails/Public/list.png", Token);
        using var probe = await _world.Bob.GetAsync($"{_world.Base}/streaming/probe?path=Public/film.mp4", Token);

        Assert.AreEqual(HttpStatusCode.NotFound, picture.StatusCode);
        Assert.AreEqual(HttpStatusCode.NotFound, again.StatusCode);
        Assert.AreEqual(HttpStatusCode.NotFound, probe.StatusCode);
        Assert.AreEqual(1, _world.Account.Calls.Count(c => c == "read Public/list.png"), "Known to have none");
    }

    private async Task<ConnectionListing> ListingAsync() =>
        (
            await _world.Bob.GetFromJsonAsync<ConnectionListing>(
                $"{_world.Base}/browse?path=Public",
                RuledConnection.Json,
                Token
            )
        )!;
}
