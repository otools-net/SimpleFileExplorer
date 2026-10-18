using System.ComponentModel;
using System.Diagnostics;
using System.Net;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using Microsoft.AspNetCore.Mvc.Testing;
using Sfe.Core.Options;
using Sfe.Core.Settings;
using Sfe.Core.Streaming;
using Sfe.Core.Thumbnails;
using Sfe.Tests.Infrastructure;

namespace Sfe.Tests;

/// <summary>
/// Videos converted by ffmpeg while they play (<see cref="VideoStreamer"/>). The tests running ffmpeg need it: set
/// <c>SFE_TEST_FFMPEG</c> to its path, or put it on PATH; they are inconclusive without it.
/// </summary>
[TestClass]
public sealed class StreamingTests
{
    private static readonly string FfmpegPath = Environment.GetEnvironmentVariable("SFE_TEST_FFMPEG") is { Length: > 0 } path ? path : "ffmpeg";

    private TestLibrary _library = null!;
    private readonly List<IDisposable> _disposables = [];

    public TestContext TestContext { get; set; } = null!;

    [TestInitialize]
    public void Initialize() => _library = new TestLibrary();

    [TestCleanup]
    public void Cleanup()
    {
        foreach (var disposable in _disposables)
            disposable.Dispose();
        _library.Dispose();
    }

    [TestMethod]
    public void TheProbeTellsWhichPicturesBrowsersPlay()
    {
        const string h264 = """
            Input #0, matroska,webm, from 'film.mkv':
              Duration: 01:02:03.50, start: 0.000000, bitrate: 2000 kb/s
              Stream #0:0(eng): Video: h264 (High), yuv420p(tv, bt709, progressive), 1920x1080, 23.98 fps (default)
              Stream #0:1(eng): Audio: ac3, 48000 Hz, 5.1(side), fltp, 384 kb/s (default)
              Stream #0:2(eng): Audio: aac (LC), 48000 Hz, stereo, fltp
            """;
        var probe = VideoStreamer.Parse(h264);
        Assert.AreEqual(3723.5, probe.Duration!.Value, 0.01);
        Assert.AreEqual("h264", probe.VideoCodec);
        Assert.IsTrue(probe.BrowserPictures);
        Assert.AreEqual("ac3", probe.AudioCodec, "The first sound stream is the one played");

        Assert.IsFalse(VideoStreamer.Parse(h264.Replace("yuv420p(tv", "yuv420p10le(tv")).BrowserPictures, "10-bit H.264");
        Assert.IsTrue(VideoStreamer.Parse(h264.Replace("yuv420p(tv", "yuvj420p(pc")).BrowserPictures, "Full-range 8-bit H.264");
        Assert.IsFalse(VideoStreamer.Parse(h264.Replace("h264 (High)", "hevc (Main)")).BrowserPictures);

        var withCover = VideoStreamer.Parse("""
              Duration: N/A, bitrate: N/A
              Stream #0:0: Video: mjpeg (Baseline), yuvj420p(pc), 600x600 (attached pic)
              Stream #0:1[0x1](und): Video: mpeg4 (Simple Profile) (mp4v / 0x7634706D), yuv420p, 320x180
            """);
        Assert.AreEqual("mpeg4", withCover.VideoCodec, "A cover picture is not the video");
        Assert.IsNull(withCover.Duration);
        Assert.IsNull(withCover.AudioCodec);
    }

    [TestMethod]
    public void TheCommandCopiesWhatBrowsersPlayAndConvertsTheRest()
    {
        var settings = new StreamingOptions { Quality = 26, MaxHeight = 720 };
        var copied = VideoStreamer.Arguments("film.mkv", 61.5, new VideoProbe(100, "h264", true, "aac"), transcoded: false, settings);
        CollectionAssert.IsSubsetOf(new[] { "-ss", "61.5", "-i", "film.mkv", "copy", "pipe:1" }, copied);
        Assert.AreEqual(2, copied.Count(a => a == "copy"), "Pictures and AAC sound copied");
        CollectionAssert.DoesNotContain(copied, "libx264");
        Assert.IsLessThan(copied.IndexOf("-i"), copied.IndexOf("-ss"), "Seeks before reading the input (fast)");

        var converted = VideoStreamer.Arguments("film.avi", 0, new VideoProbe(100, "mpeg4", false, "mp3"), transcoded: true, settings);
        CollectionAssert.DoesNotContain(converted, "-ss", "From the start: no seek");
        CollectionAssert.IsSubsetOf(new[] { "libx264", "26", "aac", "yuv420p" }, converted);
        Assert.AreEqual(@"scale=-2:min(720\,trunc(ih/2)*2)", converted[converted.IndexOf("-vf") + 1]);
        StringAssert.Contains(converted[converted.IndexOf("-movflags") + 1], "frag_keyframe");

        var keepSize = VideoStreamer.Arguments("film.avi", 0, new VideoProbe(100, "mpeg4", false, null), transcoded: true, new StreamingOptions { MaxHeight = 0 });
        Assert.AreEqual("scale=-2:trunc(ih/2)*2", keepSize[keepSize.IndexOf("-vf") + 1]);

        var soundOnly = VideoStreamer.Arguments("song.mkv", 0, new VideoProbe(100, null, false, "flac"), transcoded: true, settings);
        CollectionAssert.Contains(soundOnly, "-vn");
        CollectionAssert.DoesNotContain(soundOnly, "-vf");
    }

    [TestMethod]
    public void APlayerSeekingReplacesItsStreamAndOthersWaitForAFreeOne()
    {
        var streams = new VideoStreams();
        var viewer = new StreamViewer("Library", "alice", "10.0.0.2");
        using var first = streams.TryBegin("1:player-a", 1, "Films/a.avi", viewer, 0, true);
        Assert.IsNotNull(first);
        Assert.IsNull(streams.TryBegin("1:player-b", 1, "Films/b.avi", viewer, 0, true), "Another player waits for a free stream");

        using var seeked = streams.TryBegin("1:player-a", 1, "Films/a.avi", viewer, 300, true);
        Assert.IsNotNull(seeked, "The same player asking again replaces its stream");
        Assert.IsTrue(first.Stopping.IsCancellationRequested);
        first.Dispose();

        seeked.Sent(1000);
        seeked.Sent(24);
        var listed = streams.List().Single();
        Assert.AreEqual(("Films/a.avi", 300d, 1024L, "alice"), (listed.Path, listed.Start, listed.Bytes, listed.Viewer.User));

        seeked.Dispose();
        Assert.IsEmpty(streams.List());
        using var next = streams.TryBegin(null, 1, "Films/b.avi", viewer, 0, false);
        Assert.IsNotNull(next);
    }

    [TestMethod]
    public void TheFfmpegVersionComesFromItsFirstLine()
    {
        Assert.AreEqual("7.1-full_build-www.gyan.dev", Ffmpeg.ParseVersion("ffmpeg version 7.1-full_build-www.gyan.dev Copyright (c) 2000-2024 the FFmpeg developers\nbuilt with gcc"));
        Assert.IsNull(Ffmpeg.ParseVersion("something else"));
    }

    [TestMethod]
    public async Task WithoutFfmpegNothingIsStreamedAndTheSystemPageSaysSo()
    {
        var missing = Path.Combine(_library.BasePath, "no-ffmpeg", "ffmpeg.exe");
        var admin = await ApiClients.SetUpAsync(Factory(s => s["Sfe:Thumbnails:FfmpegPath"] = missing), TestContext.CancellationToken);
        _library.AddFile("Films/film.avi", 100);

        using var config = await GetJsonAsync(admin, "/api/config");
        Assert.AreEqual(JsonValueKind.Null, config.RootElement.GetProperty("streaming").ValueKind);
        using (var probe = await admin.GetAsync("/api/streaming/probe?path=Films/film.avi", TestContext.CancellationToken))
            Assert.AreEqual(HttpStatusCode.NotFound, probe.StatusCode);
        using (var play = await admin.GetAsync("/api/streaming/play?path=Films/film.avi", TestContext.CancellationToken))
            Assert.AreEqual(HttpStatusCode.NotFound, play.StatusCode);

        using var info = await GetJsonAsync(admin, "/api/system/info");
        Assert.AreEqual(Path.TrimEndingDirectorySeparator(_library.Root), Path.TrimEndingDirectorySeparator(info.RootElement.GetProperty("root").GetString()!));
        var ffmpeg = info.RootElement.GetProperty("ffmpeg");
        Assert.AreEqual(missing, ffmpeg.GetProperty("configured").GetString());
        Assert.AreEqual(JsonValueKind.Null, ffmpeg.GetProperty("path").ValueKind);

        using var sessions = await GetJsonAsync(admin, "/api/streaming/sessions");
        Assert.IsFalse(sessions.RootElement.GetProperty("available").GetBoolean());
        Assert.AreEqual("fallback", sessions.RootElement.GetProperty("mode").GetString());
    }

    [TestMethod]
    public async Task VisitorsAndPeopleWithoutTheRightGetNoStreamsNorTheirList()
    {
        var factory = Factory();
        var admin = await ApiClients.SetUpAsync(factory, TestContext.CancellationToken);
        var visitor = ApiClients.Create(factory);
        using (var response = await visitor.GetAsync("/api/streaming/sessions", TestContext.CancellationToken))
            Assert.AreEqual(HttpStatusCode.Unauthorized, response.StatusCode);
        using (var response = await visitor.GetAsync("/api/system/info", TestContext.CancellationToken))
            Assert.AreEqual(HttpStatusCode.Unauthorized, response.StatusCode);
        using (var response = await admin.GetAsync("/api/streaming/sessions", TestContext.CancellationToken))
            Assert.AreEqual(HttpStatusCode.OK, response.StatusCode);
    }

    [TestMethod]
    public async Task StreamingSettingsAreCheckedAndTheServerKeepsItsOwn()
    {
        Assert.AreEqual(SettingLevel.Tenant, SettingCatalog.Find("streaming.mode")!.Scope);
        foreach (var key in new[] { "streaming.maxStreams", "streaming.quality", "streaming.maxHeight", "streaming.copyVideo" })
            Assert.AreEqual(SettingLevel.Server, SettingCatalog.Find(key)!.Scope, key);

        var admin = await ApiClients.SetUpAsync(Factory(), TestContext.CancellationToken);
        using var current = await GetJsonAsync(admin, "/api/settings");
        var values = JsonSerializer.Deserialize<Dictionary<string, JsonElement>>(current.RootElement.GetProperty("values").GetRawText())!;
        var streaming = JsonSerializer.Deserialize<Dictionary<string, JsonElement>>(values["streaming"].GetRawText())!;
        Assert.AreEqual("fallback", streaming["mode"].GetString());

        values["streaming"] = JsonSerializer.SerializeToElement(new { mode = "always", maxStreams = 3, quality = 23, maxHeight = 100, copyVideo = true });
        using (var invalid = await admin.PutAsJsonAsync("/api/settings", values, TestContext.CancellationToken))
        {
            Assert.AreEqual(HttpStatusCode.BadRequest, invalid.StatusCode);
            StringAssert.Contains(await invalid.Content.ReadAsStringAsync(TestContext.CancellationToken), "streaming.maxHeight");
        }

        values["streaming"] = JsonSerializer.SerializeToElement(new { mode = "always", maxStreams = 3, quality = 23, maxHeight = 0, copyVideo = true });
        using (var saved = await admin.PutAsJsonAsync("/api/settings", values, TestContext.CancellationToken))
            Assert.AreEqual(HttpStatusCode.OK, saved.StatusCode, await saved.Content.ReadAsStringAsync(TestContext.CancellationToken));
        StringAssert.Contains(await File.ReadAllTextAsync(_library.SettingsPath, TestContext.CancellationToken), "\"Always\"",
            "The settings file names the mode");
    }

    [TestMethod]
    public async Task AVideoPlaysConvertedFromThePositionAskedAndIsListedMeanwhile()
    {
        await RenderAsync("Films/clip.avi", "-c:v", "mpeg4", "-c:a", "pcm_s16le");
        var admin = await ApiClients.SetUpAsync(Factory(s => s["Sfe:Thumbnails:FfmpegPath"] = FfmpegPath), TestContext.CancellationToken);

        using (var config = await GetJsonAsync(admin, "/api/config"))
            Assert.IsFalse(config.RootElement.GetProperty("streaming").GetProperty("always").GetBoolean());
        using (var probe = await GetJsonAsync(admin, "/api/streaming/probe?path=Films/clip.avi"))
        {
            Assert.AreEqual(4, probe.RootElement.GetProperty("duration").GetDouble(), 0.1);
            Assert.IsTrue(probe.RootElement.GetProperty("transcoded").GetBoolean());
        }

        var output = Path.Combine(_library.BasePath, "streamed.mp4");
        using (var response = await admin.GetAsync("/api/streaming/play?path=Films/clip.avi&t=1&id=test", HttpCompletionOption.ResponseHeadersRead, TestContext.CancellationToken))
        {
            Assert.AreEqual(HttpStatusCode.OK, response.StatusCode, await ErrorAsync(response));
            Assert.AreEqual("video/mp4", response.Content.Headers.ContentType?.MediaType);
            await using var body = await response.Content.ReadAsStreamAsync(TestContext.CancellationToken);
            var head = new byte[8];
            await body.ReadExactlyAsync(head, TestContext.CancellationToken);
            Assert.AreEqual("ftyp", Encoding.ASCII.GetString(head, 4, 4), "An MP4 file");

            using (var sessions = await GetJsonAsync(admin, "/api/streaming/sessions"))
            {
                var stream = sessions.RootElement.GetProperty("streams").EnumerateArray().Single();
                Assert.AreEqual("Films/clip.avi", stream.GetProperty("path").GetString());
                Assert.AreEqual(1, stream.GetProperty("start").GetDouble());
                Assert.IsTrue(stream.GetProperty("transcoded").GetBoolean());
                Assert.AreEqual(ApiClients.AdminName, stream.GetProperty("viewer").GetProperty("user").GetString());
            }

            await using var file = File.Create(output);
            await file.WriteAsync(head, TestContext.CancellationToken);
            await body.CopyToAsync(file, TestContext.CancellationToken);
        }

        // No output file: ffmpeg describes the input and stops.
        var (_, errors) = await RunFfmpegAsync("-hide_banner", "-nostdin", "-i", output);
        StringAssert.Contains(errors, "Video: h264", "The pictures are H.264");
        StringAssert.Contains(errors, "Audio: aac");
        Assert.AreEqual(3, Duration(errors), 0.3, "Converted from 1 s on, until the end: " + errors);

        var deadline = DateTime.UtcNow.AddSeconds(10);
        while (true)
        {
            using var sessions = await GetJsonAsync(admin, "/api/streaming/sessions");
            if (sessions.RootElement.GetProperty("streams").GetArrayLength() == 0)
                break;
            Assert.IsLessThan(deadline, DateTime.UtcNow, "The ended stream left the list");
            await Task.Delay(100, TestContext.CancellationToken);
        }
    }

    [TestMethod]
    public async Task BrowserPicturesAreCopiedAndALeavingViewerStopsFfmpeg()
    {
        // Noise makes the copy (~1.4 MB) larger than the response pipe holds: ffmpeg waits for the reader instead of
        // finishing the stream before the list is asked for (a plain 4 s clip is ~50 KB, copied in milliseconds).
        await RenderAsync("Films/clip.mp4", "-vf", "noise=alls=60:allf=t", "-c:v", "libx264", "-pix_fmt", "yuv420p", "-c:a", "aac");
        var admin = await ApiClients.SetUpAsync(Factory(s => s["Sfe:Thumbnails:FfmpegPath"] = FfmpegPath), TestContext.CancellationToken);

        using (var probe = await GetJsonAsync(admin, "/api/streaming/probe?path=Films/clip.mp4"))
            Assert.IsFalse(probe.RootElement.GetProperty("transcoded").GetBoolean(), "H.264 pictures are copied");

        using (var response = await admin.GetAsync("/api/streaming/play?path=Films/clip.mp4", HttpCompletionOption.ResponseHeadersRead, TestContext.CancellationToken))
        {
            Assert.AreEqual(HttpStatusCode.OK, response.StatusCode, await ErrorAsync(response));
            using var sessions = await GetJsonAsync(admin, "/api/streaming/sessions");
            Assert.IsFalse(sessions.RootElement.GetProperty("streams")[0].GetProperty("transcoded").GetBoolean());
        }

        // Left after the first bytes: the stream ends and leaves the list.
        var deadline = DateTime.UtcNow.AddSeconds(10);
        while (true)
        {
            using var sessions = await GetJsonAsync(admin, "/api/streaming/sessions");
            if (sessions.RootElement.GetProperty("streams").GetArrayLength() == 0)
                break;
            Assert.IsLessThan(deadline, DateTime.UtcNow, "The stream the viewer left ended");
            await Task.Delay(100, TestContext.CancellationToken);
        }
    }

    /// <summary>Seconds of the "Duration:" line of ffmpeg's description of a file.</summary>
    private static double Duration(string probeOutput)
    {
        var duration = System.Text.RegularExpressions.Regex.Match(probeOutput, @"Duration: (\d+):(\d{2}):(\d{2}(?:\.\d+)?)");
        Assert.IsTrue(duration.Success, probeOutput);
        return int.Parse(duration.Groups[1].Value) * 3600 + int.Parse(duration.Groups[2].Value) * 60
            + double.Parse(duration.Groups[3].Value, System.Globalization.CultureInfo.InvariantCulture);
    }

    private async Task RenderAsync(string relativePath, params string[] codecs)
    {
        var target = _library.FullPath(relativePath);
        Directory.CreateDirectory(Path.GetDirectoryName(target)!);
        var (exitCode, errors) = await RunFfmpegAsync([
            "-hide_banner", "-loglevel", "error", "-nostdin", "-y",
            "-f", "lavfi", "-i", "testsrc=duration=4:size=320x180:rate=10", "-f", "lavfi", "-i", "sine=frequency=440:duration=4",
            .. codecs, "-shortest", target]);
        Assert.AreEqual(0, exitCode, errors);
    }

    /// <summary>Runs ffmpeg as the tests find it; inconclusive when there is none.</summary>
    private async Task<(int ExitCode, string Errors)> RunFfmpegAsync(params string[] arguments)
    {
        var start = new ProcessStartInfo(FfmpegPath) { RedirectStandardError = true, UseShellExecute = false, CreateNoWindow = true };
        foreach (var argument in arguments)
            start.ArgumentList.Add(argument);
        Process process;
        try
        {
            process = Process.Start(start)!;
        }
        catch (Win32Exception)
        {
            Assert.Inconclusive("ffmpeg not found: set SFE_TEST_FFMPEG to run the streaming tests.");
            throw;
        }

        using (process)
        {
            var errors = await process.StandardError.ReadToEndAsync(TestContext.CancellationToken);
            await process.WaitForExitAsync(TestContext.CancellationToken);
            return (process.ExitCode, errors);
        }
    }

    private WebApplicationFactory<Program> Factory(Action<IDictionary<string, string?>>? configure = null)
    {
        var factory = ApiClients.CreateFactory(_library, configure);
        _disposables.Add(factory);
        return factory;
    }

    private async Task<JsonDocument> GetJsonAsync(HttpClient client, string url)
    {
        using var response = await client.GetAsync(url, TestContext.CancellationToken);
        var text = await response.Content.ReadAsStringAsync(TestContext.CancellationToken);
        Assert.AreEqual(HttpStatusCode.OK, response.StatusCode, $"{url}: {text}");
        return JsonDocument.Parse(text);
    }

    private async Task<string> ErrorAsync(HttpResponseMessage response) =>
        response.IsSuccessStatusCode ? string.Empty : await response.Content.ReadAsStringAsync(TestContext.CancellationToken);
}
