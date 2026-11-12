using Sfe.Core.Indexing;
using Sfe.Core.Thumbnails;
using Sfe.Tests.Infrastructure;

namespace Sfe.Tests;

/// <summary>
/// What ffmpeg may open when given a file of the library (audit 2026-11, A4). The tests running ffmpeg need it: set
/// <c>SFE_TEST_FFMPEG</c> to its path, or put it on PATH; inconclusive without it.
/// </summary>
[TestClass]
public sealed class FfmpegInputTests
{
    /// <summary>As ffmpeg 7 and later print it: three flag columns, devices marked <c>d</c>.</summary>
    private const string NewList = """
        Formats:
         D.. = Demuxing supported
         .E. = Muxing supported
         ..d = Is a device
         ---
         D   aac             raw ADTS AAC (Advanced Audio Coding)
         D   avi             AVI (Audio Video Interleaved)
         D   avisynth        AviSynth script
         D   concat          Virtual concatenation script
         D   dash            Dynamic Adaptive Streaming over HTTP
         D d dshow           DirectShow capture
         D   flac            raw FLAC
         D   flv             FLV (Flash Video)
         D   gif             CompuServe Graphics Interchange Format (GIF)
         D   hls             Apple HTTP Live Streaming
         D   image2          image2 sequence
         D   jpeg_pipe       piped jpeg sequence
         D d lavfi           Libavfilter virtual input device
         D   matroska,webm   Matroska / WebM
         D   mov,mp4,m4a,3gp,3g2,mj2 QuickTime / MOV
         D   mp3             MP2/3 (MPEG audio layer 2/3)
         D   mpeg            MPEG-PS (MPEG-2 Program Stream)
         D   mpegts          MPEG-TS (MPEG-2 Transport Stream)
         D   ogg             Ogg
         D   png_pipe        piped png sequence
         D   rm              RealMedia
         D   rtsp            RTSP input
         D   sdp             SDP
         D   wav             WAV / WAVE (Waveform Audio)
         D   webp_pipe       piped webp sequence
         D   asf             ASF (Advanced / Active Streaming Format)
         D   mxf             MXF (Material eXchange Format)
         D   mjpeg           raw MJPEG video
         D   h264            raw H.264 video
         D   hevc            raw HEVC video
        """;

    private TestLibrary _library = null!;

    public TestContext TestContext { get; set; } = null!;

    [TestInitialize]
    public void Initialize() => _library = new TestLibrary();

    [TestCleanup]
    public void Cleanup() => _library.Dispose();

    [TestMethod]
    public void RealMediaFormatsAreAllowedAndScriptsAndDevicesAreNot()
    {
        var allowed = FfmpegDemuxers.AllowedList(NewList)!.Split(',');

        foreach (var format in new[] { "mov", "mp4", "matroska", "webm", "avi", "image2", "jpeg_pipe", "mp3", "mpegts" })
            CollectionAssert.Contains(allowed, format);
        foreach (var refused in new[] { "concat", "hls", "dash", "avisynth", "sdp", "rtsp", "dshow", "lavfi" })
            CollectionAssert.DoesNotContain(allowed, refused);
    }

    [TestMethod]
    public void TheListOfAnOlderFfmpegIsReadToo()
    {
        // Before ffmpeg 7: two flag columns, the names one character earlier.
        var older = NewList.Replace(" D   ", " D  ").Replace(" D d ", " D  ");

        var allowed = FfmpegDemuxers.AllowedList(older)!.Split(',');

        CollectionAssert.Contains(allowed, "matroska");
        CollectionAssert.DoesNotContain(allowed, "concat");
        CollectionAssert.DoesNotContain(allowed, "dshow", "Refused by name where the list does not mark devices");
    }

    [TestMethod]
    [DataRow("")]
    [DataRow("Unrecognized option demuxers.\nError splitting the argument list: Option not found\n")]
    [DataRow(" ---\n D   mov,mp4 QuickTime\n D   avi AVI\n")]
    public void AnOutputThatIsNotTheListRestrictsNothing(string output)
    {
        Assert.IsNull(FfmpegDemuxers.AllowedList(output));
    }

    [TestMethod]
    public async Task ALibraryFileIsOpenedFromTheDiskByAReaderOfRealMedia()
    {
        await using var services = await ServicesWithFfmpegAsync();

        var input = await services.Get<Ffmpeg>().LibraryInputAsync("film.mkv");

        CollectionAssert.AreEqual(new[] { "-protocol_whitelist", "file", "-format_whitelist" }, input.Take(3).ToArray());
        CollectionAssert.AreEqual(new[] { "-i", "film.mkv" }, input.Skip(4).ToArray());
        CollectionAssert.DoesNotContain(input[3].Split(','), "concat");
        CollectionAssert.Contains(input[3].Split(','), "matroska");
    }

    [TestMethod]
    [DataRow("testsrc-1s-320x180.mp4", "clip.mp4")]
    [DataRow("testsrc-4s-mpeg4-pcm.avi", "clip.avi")]
    [DataRow("sine-440hz-2s.wav", "song.wav")]
    [DataRow("testsrc-64x48-still.png", "picture.png")]
    [DataRow("testsrc-64x48-5frames.gif", "picture.gif")]
    public async Task RealMediaIsStillRead(string sample, string name)
    {
        await using var services = await ServicesWithFfmpegAsync();
        var file = TestSamples.CopyTo(sample, _library.FullPath($"Clips/{name}"));

        var described = await services.Get<MediaDimensions>().ProbeAsync(file, TestContext.CancellationToken);

        StringAssert.Contains(described, "Stream #0", described);
    }

    [TestMethod]
    public async Task AScriptNamedAsAVideoOpensNoOtherFile()
    {
        await using var services = await ServicesWithFfmpegAsync();
        TestSamples.CopyTo("testsrc-1s-320x180.mp4", _library.FullPath("Clips/Private/secret.mp4"));
        var script = _library.FullPath("Clips/innocent.mp4");
        await File.WriteAllTextAsync(script, "ffconcat version 1.0\nfile Private/secret.mp4\n", TestContext.CancellationToken);

        var described = await services.Get<MediaDimensions>().ProbeAsync(script, TestContext.CancellationToken);

        Assert.IsFalse(described.Contains("Stream #0", StringComparison.Ordinal), described);
        Assert.IsFalse(described.Contains("320x180", StringComparison.Ordinal), described);
    }

    [TestMethod]
    public async Task APlaylistNamedAsAVideoReachesNoAddress()
    {
        await using var services = await ServicesWithFfmpegAsync();
        var playlist = _library.FullPath("Clips/stream.mp4");
        Directory.CreateDirectory(Path.GetDirectoryName(playlist)!);
        // Port 9 (discard): nothing answers; an ffmpeg that tried would say "Connection refused" or time out.
        await File.WriteAllTextAsync(
            playlist,
            "#EXTM3U\n#EXT-X-TARGETDURATION:10\n#EXTINF:10,\nhttp://127.0.0.1:9/segment.ts\n#EXT-X-ENDLIST\n",
            TestContext.CancellationToken);

        var described = await services.Get<MediaDimensions>().ProbeAsync(playlist, TestContext.CancellationToken);

        Assert.IsFalse(described.Contains("Stream #0", StringComparison.Ordinal), described);
        Assert.IsFalse(described.Contains("Connection", StringComparison.OrdinalIgnoreCase), described);
    }

    private async Task<CoreServices> ServicesWithFfmpegAsync()
    {
        var services = await CoreServices.CreateAsync(_library, settings =>
            settings["Sfe:Thumbnails:FfmpegPath"] =
                Environment.GetEnvironmentVariable("SFE_TEST_FFMPEG") is { Length: > 0 } path ? path : "ffmpeg");
        if (!services.Get<Ffmpeg>().IsAvailable)
        {
            await services.DisposeAsync();
            Assert.Inconclusive("ffmpeg not found: set SFE_TEST_FFMPEG to run the tests of what it may open.");
        }

        return services;
    }
}
