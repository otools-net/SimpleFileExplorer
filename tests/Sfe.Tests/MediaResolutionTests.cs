using System.Buffers.Binary;
using Microsoft.EntityFrameworkCore;
using Sfe.Core.Data;
using Sfe.Core.Indexing;
using Sfe.Core.Library;
using Sfe.Core.Search;
using Sfe.Core.Tagging;
using Sfe.Core.Thumbnails;
using Sfe.Tests.Infrastructure;

namespace Sfe.Tests;

/// <summary>Resolution of images (read from their headers) and videos (ffmpeg), kept in the index and listed.</summary>
[TestClass]
public sealed class MediaResolutionTests
{
    private TestLibrary _library = null!;
    private CoreServices _services = null!;

    public TestContext TestContext { get; set; } = null!;

    [TestInitialize]
    public async Task InitializeAsync()
    {
        _library = new TestLibrary();
        _services = await CoreServices.CreateAsync(_library);
    }

    [TestCleanup]
    public async Task CleanupAsync()
    {
        await _services.DisposeAsync();
        _library.Dispose();
    }

    /* ---------- Image headers ---------- */

    [TestMethod]
    public void ReadsPng() => Assert.AreEqual(new PixelSize(640, 480), Header(Png(640, 480)));

    [TestMethod]
    public void ReadsGif() => Assert.AreEqual(new PixelSize(300, 200), Header([.. "GIF89a"u8, 0x2C, 0x01, 0xC8, 0x00, .. new byte[8]]));

    [TestMethod]
    public void ReadsBmpWithTopDownRows()
    {
        var bmp = new byte[54];
        "BM"u8.CopyTo(bmp);
        BinaryPrimitives.WriteUInt32LittleEndian(bmp.AsSpan(14), 40);
        BinaryPrimitives.WriteInt32LittleEndian(bmp.AsSpan(18), 800);
        BinaryPrimitives.WriteInt32LittleEndian(bmp.AsSpan(22), -600);
        Assert.AreEqual(new PixelSize(800, 600), Header(bmp));
    }

    [TestMethod]
    public void ReadsTheThreeKindsOfWebP()
    {
        var lossy = WebP("VP8 ");
        lossy[23] = 0x9D;
        lossy[24] = 0x01;
        lossy[25] = 0x2A;
        BinaryPrimitives.WriteUInt16LittleEndian(lossy.AsSpan(26), 1024);
        BinaryPrimitives.WriteUInt16LittleEndian(lossy.AsSpan(28), 768);
        Assert.AreEqual(new PixelSize(1024, 768), Header(lossy));

        var lossless = WebP("VP8L");
        lossless[20] = 0x2F;
        BinaryPrimitives.WriteUInt32LittleEndian(lossless.AsSpan(21), (uint)(1920 - 1 | (1080 - 1) << 14));
        Assert.AreEqual(new PixelSize(1920, 1080), Header(lossless));

        var extended = WebP("VP8X");
        WriteUInt24(extended, 24, 4000 - 1);
        WriteUInt24(extended, 27, 3000 - 1);
        Assert.AreEqual(new PixelSize(4000, 3000), Header(extended));
    }

    [TestMethod]
    [DataRow(0, 4032, 3024)]
    [DataRow(1, 4032, 3024)]
    [DataRow(3, 4032, 3024)]
    [DataRow(6, 3024, 4032)]
    [DataRow(8, 3024, 4032)]
    public void ReadsJpegTurnedByItsExifOrientation(int orientation, int width, int height) =>
        Assert.AreEqual(new PixelSize(width, height), Header(Jpeg(4032, 3024, orientation)));

    [TestMethod]
    public void ReadsBigEndianExif() =>
        Assert.AreEqual(new PixelSize(30, 40), Header(Jpeg(40, 30, orientation: 6, bigEndian: true)));

    [TestMethod]
    public void TruncatedOrFakeImagesAreUnknownAndOtherFormatsAreLeftToFfmpeg()
    {
        Assert.AreEqual(PixelSize.Unknown, Header([0xFF, 0xD8, 0xFF, 0xE0, 0x00, 0x10, 0x4A]));
        Assert.AreEqual(PixelSize.Unknown, Header(Png(0, 480)));
        Assert.IsNull(Header("II*\0 a TIFF image"u8.ToArray()));
        Assert.IsNull(Header([]));
    }

    /* ---------- ffmpeg output ---------- */

    [TestMethod]
    public void ReadsTheFirstVideoStreamOfFfmpeg()
    {
        const string output = """
            Input #0, mov,mp4,m4a,3gp,3g2,mj2, from 'clip.mp4':
              Duration: 00:00:10.00, start: 0.000000, bitrate: 1205 kb/s
              Stream #0:0[0x1](und): Audio: aac (LC) (mp4a / 0x6134706D), 48000 Hz, stereo, fltp, 128 kb/s (default)
              Stream #0:1[0x2](und): Video: h264 (High) (avc1 / 0x31637661), yuv420p(tv, bt709, progressive), 1920x1080 [SAR 1:1 DAR 16:9], 1070 kb/s, 30 fps (default)
            At least one output file must be specified
            """;
        Assert.AreEqual(new PixelSize(1920, 1080), MediaDimensions.ParseFfmpegOutput(output));
    }

    [TestMethod]
    public void PortraitPhoneVideosAreTurned()
    {
        const string output = """
              Stream #0:0[0x1](eng): Video: hevc (Main) (hvc1 / 0x31637668), yuv420p(tv, bt709), 1920x1080, 9000 kb/s, 29.97 fps (default)
                Metadata:
                  handler_name    : Core Media Video
                Side data:
                  displaymatrix: rotation of -90.00 degrees
              Stream #0:1[0x2](eng): Audio: aac (LC) (mp4a / 0x6134706D), 44100 Hz, mono, fltp, 96 kb/s (default)
            """;
        Assert.AreEqual(new PixelSize(1080, 1920), MediaDimensions.ParseFfmpegOutput(output));
    }

    [TestMethod]
    public void CoverPicturesAreNotTheVideo()
    {
        const string output = """
              Stream #0:0: Video: mjpeg (Baseline), yuvj420p(pc, bt470bg/unknown/unknown), 600x600 [SAR 1:1 DAR 1:1], 90k tbr, 90k tbn (attached pic)
              Stream #0:1: Video: vp9 (Profile 0), yuv420p(tv, bt709), 3840x2160, SAR 1:1 DAR 16:9, 25 fps, 25 tbr, 1k tbn (default)
            """;
        Assert.AreEqual(new PixelSize(3840, 2160), MediaDimensions.ParseFfmpegOutput(output));
        Assert.AreEqual(PixelSize.Unknown, MediaDimensions.ParseFfmpegOutput("clip.mkv: Invalid data found when processing input"));
    }

    [TestMethod]
    public void ReadsTheDurationOfFfmpeg()
    {
        const string output = """
            Input #0, matroska,webm, from 'movie.mkv':
              Metadata:
                title           : Duration: 99:99:99
              Duration: 01:32:05.48, start: 0.000000, bitrate: 2205 kb/s
              Stream #0:0: Video: h264 (High), yuv420p(progressive), 1920x800, SAR 1:1 DAR 12:5, 23.98 fps (default)
            """;
        Assert.AreEqual(5525.48, MediaDimensions.ParseDuration(output));
        Assert.AreEqual(0, MediaDimensions.ParseDuration("  Duration: N/A, start: 0.000000, bitrate: N/A"));
        Assert.AreEqual(0, MediaDimensions.ParseDuration("clip.mkv: Invalid data found when processing input"));
    }

    /* ---------- Index ---------- */

    [TestMethod]
    public async Task ListingsCarryTheDurationOfVideos()
    {
        await ScanAsync();
        await using (var db = await CreateDbAsync())
        {
            var holiday = _library.FullPath("Media/Holiday 2020.mp4");
            await db.Files.Where(f => f.FullName == holiday).ExecuteUpdateAsync(s => s.SetProperty(f => f.Duration, 61.5), TestContext.CancellationToken);
        }

        var listing = _services.Get<LibraryBrowser>().List("Media", 0, 10)!;
        var listed = await _services.Get<TagService>().AttachIndexDataAsync(listing.Items, TestContext.CancellationToken);
        Assert.AreEqual(61.5, listed.Single(i => i.Name == "Holiday 2020.mp4").Duration);
        Assert.IsNull(listed.Single(i => i.Name == "photo.jpg").Duration);
    }

    [TestMethod]
    public async Task AddsTheDurationColumnToAnExistingSqliteIndex()
    {
        // The index as the version before durations created it, with a file in it.
        await ScanAsync();
        await using (var db = await CreateDbAsync())
        {
            await db.Database.ExecuteSqlAsync($"DROP INDEX IX_Files_Duration", TestContext.CancellationToken);
            await db.Database.ExecuteSqlAsync($"ALTER TABLE Files DROP COLUMN Duration", TestContext.CancellationToken);
        }

        await _services.Get<DatabaseInitializer>().InitializeAsync(TestContext.CancellationToken);

        await using (var db = await CreateDbAsync())
        {
            Assert.IsTrue(await db.Files.AnyAsync(TestContext.CancellationToken), "The indexed files are kept.");
            Assert.IsFalse(await db.Files.AnyAsync(f => f.Duration != null, TestContext.CancellationToken), "Read again by the next scan.");
        }
        Assert.IsNotEmpty(await SearchAsync("holiday -d:>1h"));
    }

    [TestMethod]
    public async Task AddsThePixelsColumnAndTheSortIndexesToAnExistingSqliteIndex()
    {
        // The index as 1.35 created it: no Pixels column, no duration or pixels index, resolutions already read.
        await ScanAsync();
        var holiday = _library.FullPath("Media/Holiday 2020.mp4");
        await using (var db = await CreateDbAsync())
        {
            await db.Database.ExecuteSqlAsync($"DROP INDEX IX_Files_Pixels", TestContext.CancellationToken);
            await db.Database.ExecuteSqlAsync($"DROP INDEX IX_Files_Duration", TestContext.CancellationToken);
            await db.Database.ExecuteSqlAsync($"ALTER TABLE Files DROP COLUMN Pixels", TestContext.CancellationToken);
            await db.Database.ExecuteSqlAsync($"UPDATE Files SET Width = 1920, Height = 1080, Duration = 60 WHERE FullName = {holiday}", TestContext.CancellationToken);
        }

        await _services.Get<DatabaseInitializer>().InitializeAsync(TestContext.CancellationToken);

        await using (var db = await CreateDbAsync())
        {
            var indexes = await db.Database.SqlQuery<string>($"SELECT name AS Value FROM sqlite_master WHERE type = 'index'").ToListAsync(TestContext.CancellationToken);
            CollectionAssert.IsSubsetOf(new[] { "IX_Files_Pixels", "IX_Files_Duration" }, indexes);
            Assert.AreEqual(1920L * 1080, await db.Files.Where(f => f.FullName == holiday).Select(f => f.Pixels).SingleAsync(TestContext.CancellationToken),
                "Computed for the existing rows.");
        }

        var search = _services.Get<FileSearchService>();
        foreach (var sort in new[] { SortField.Resolution, SortField.Duration })
        {
            var first = (await search.SearchAsync("", "", null, 1, sort, true, TestContext.CancellationToken))!;
            Assert.AreEqual("Media/Holiday 2020.mp4", first.Items.Single().Path, $"{sort}");
        }

        // A later scan writes the file again; the database keeps computing the column.
        File.SetLastWriteTimeUtc(holiday, DateTime.UtcNow.AddMinutes(-5));
        await ScanAsync();
        await using (var db = await CreateDbAsync())
        {
            var row = await db.Files.Where(f => f.FullName == holiday).Select(f => new { f.Width, f.Height, f.Pixels }).SingleAsync(TestContext.CancellationToken);
            Assert.AreEqual((long?)row.Width * row.Height, row.Pixels);
        }
    }

    [TestMethod]
    [DataRow(SortField.Duration, "abc/RFile1.txt")]
    [DataRow(SortField.Duration, "NaN/RFile1.txt")]
    [DataRow(SortField.Resolution, "-5/RFile1.txt")]
    [DataRow(SortField.Resolution, "RFile1.txt")]
    public async Task SearchRejectsMalformedDurationAndResolutionCursors(SortField sort, string after)
    {
        await ScanAsync();

        Assert.IsNull(await _services.Get<FileSearchService>().SearchAsync("", "", after, 10, sort, cancellationToken: TestContext.CancellationToken));
    }

    [TestMethod]
    public async Task ScansStoreTheResolutionOfImagesAndListingsShowIt()
    {
        await WriteAsync("Photos/wide.png", Png(1600, 900));
        await WriteAsync("Photos/portrait.jpg", Jpeg(4032, 3024, orientation: 6));
        await ScanAsync();

        await using (var db = await CreateDbAsync())
        {
            Assert.AreEqual((1600, 900), await SizeAsync(db, "Photos/wide.png"));
            Assert.AreEqual((3024, 4032), await SizeAsync(db, "Photos/portrait.jpg"));
            // The fixture's photo.jpg is text: left to ffmpeg, which finds no picture in it; without ffmpeg, read again
            // by the next scan.
            var ffmpeg = _services.Get<Ffmpeg>().IsAvailable;
            Assert.AreEqual(ffmpeg ? (0, 0) : (null, null), await SizeAsync(db, "Media/photo.jpg"));
            Assert.AreEqual((null, null), await SizeAsync(db, "RFile1.txt"));
        }

        var listing = _services.Get<LibraryBrowser>().List("Photos", 0, 10)!;
        var listed = await _services.Get<TagService>().AttachIndexDataAsync(listing.Items, TestContext.CancellationToken);
        var wide = listed.Single(i => i.Name == "wide.png");
        Assert.AreEqual((1600, 900), (wide.Width, wide.Height));

        var found = (await SearchAsync("wide")).Single();
        Assert.AreEqual((1600, 900), (found.Width, found.Height));
        var unknown = (await SearchAsync("photo e:jpg")).Single();
        Assert.IsNull(unknown.Width);
    }

    [TestMethod]
    public async Task UnreadableSizesAreNotListedNorMatched()
    {
        await WriteAsync("Photos/broken.png", Png(0, 0));
        await ScanAsync();

        await using (var db = await CreateDbAsync())
            Assert.AreEqual((0, 0), await SizeAsync(db, "Photos/broken.png"), "Read once, not understood: not read again.");
        Assert.IsNull((await SearchAsync("broken")).Single().Width, "Not listed as 0×0.");
        Assert.IsEmpty(await SearchAsync("broken w:<100"));
        Assert.IsEmpty(await SearchAsync("broken r:<=10x10"));
    }

    [TestMethod]
    public async Task AChangedImageIsReadAgain()
    {
        var path = await WriteAsync("Photos/image.png", Png(100, 50));
        await ScanAsync();
        await File.WriteAllBytesAsync(path, Png(200, 150), TestContext.CancellationToken);
        File.SetLastWriteTime(path, DateTime.Now.AddMinutes(1));
        await ScanAsync();

        await using var db = await CreateDbAsync();
        Assert.AreEqual((200, 150), await SizeAsync(db, "Photos/image.png"));
    }

    [TestMethod]
    public async Task FilesIndexedWithoutResolutionsAreReadOnceTurnedOn()
    {
        await using (var off = await CoreServices.CreateAsync(_library, s => s["Sfe:Scan:ReadResolution"] = "false"))
        {
            await WriteAsync("Photos/image.png", Png(100, 50));
            var progress = new ScanProgress();
            await off.Get<DirectoryIndexer>().IndexAsync(_library.Root, true, progress, TestContext.CancellationToken);
        }

        await using (var db = await CreateDbAsync())
            Assert.AreEqual((null, null), await SizeAsync(db, "Photos/image.png"));

        await ScanAsync();
        await using (var db = await CreateDbAsync())
            Assert.AreEqual((100, 50), await SizeAsync(db, "Photos/image.png"));
    }

    [TestMethod]
    public async Task ImagesTaggedBeforeAnyScanGetTheirResolution()
    {
        await WriteAsync("Photos/image.png", Png(64, 32));
        var ids = await _services.Get<DirectoryIndexer>().IndexFilesAsync([_library.FullPath("Photos/image.png")], TestContext.CancellationToken);

        await using var db = await CreateDbAsync();
        var file = await db.Files.SingleAsync(f => f.Id == ids.Values.Single(), TestContext.CancellationToken);
        Assert.AreEqual((64, 32), (file.Width, file.Height));
    }

    [TestMethod]
    public async Task AddsTheResolutionColumnsToAnExistingSqliteIndex()
    {
        // The index as the version before resolutions created it.
        await using (var db = await CreateDbAsync())
        {
            // Its resolution sort came later still: column and index go first.
            await db.Database.ExecuteSqlAsync($"DROP INDEX IX_Files_Pixels", TestContext.CancellationToken);
            await db.Database.ExecuteSqlAsync($"ALTER TABLE Files DROP COLUMN Pixels", TestContext.CancellationToken);
            await db.Database.ExecuteSqlAsync($"ALTER TABLE Files DROP COLUMN Width", TestContext.CancellationToken);
            await db.Database.ExecuteSqlAsync($"ALTER TABLE Files DROP COLUMN Height", TestContext.CancellationToken);
        }

        await _services.Get<DatabaseInitializer>().InitializeAsync(TestContext.CancellationToken);
        await WriteAsync("Photos/image.png", Png(10, 20));
        await ScanAsync();

        Assert.AreEqual(10, (await SearchAsync("r:10x20")).Single().Width);
    }

    [TestMethod]
    public async Task ScansReadTheResolutionOfVideosWithFfmpeg()
    {
        await using var services = await CoreServices.CreateAsync(_library, s =>
            s["Sfe:Thumbnails:FfmpegPath"] = Environment.GetEnvironmentVariable("SFE_TEST_FFMPEG") is { Length: > 0 } path ? path : "ffmpeg");
        var ffmpeg = services.Get<Ffmpeg>();
        if (!ffmpeg.IsAvailable)
            Assert.Inconclusive("ffmpeg not found: set SFE_TEST_FFMPEG to run the video resolution test.");

        var clip = _library.FullPath("Clips/clip.mp4");
        Directory.CreateDirectory(Path.GetDirectoryName(clip)!);
        var (exitCode, errors) = await ffmpeg.RunAsync(
            ["-hide_banner", "-loglevel", "error", "-nostdin", "-y", "-f", "lavfi", "-i", "testsrc=duration=1:size=320x180:rate=10", "-pix_fmt", "yuv420p", clip],
            TimeSpan.FromSeconds(30), cancellationToken: TestContext.CancellationToken);
        Assert.AreEqual(0, exitCode, errors);

        await services.Get<DirectoryIndexer>().IndexAsync(_library.FullPath("Clips"), true, new ScanProgress(), TestContext.CancellationToken);

        await using (var db = await CreateDbAsync())
        {
            Assert.AreEqual((320, 180), await SizeAsync(db, "Clips/clip.mp4"));
            Assert.AreEqual(1, (await DurationAsync(db, "Clips/clip.mp4"))!.Value, 0.05);

            // A video indexed by the version that only read resolutions: the next scan reads its duration.
            var fullName = _library.FullPath("Clips/clip.mp4");
            await db.Files.Where(f => f.FullName == fullName).ExecuteUpdateAsync(s => s.SetProperty(f => f.Duration, (double?)null), TestContext.CancellationToken);
        }

        await services.Get<DirectoryIndexer>().IndexAsync(_library.FullPath("Clips"), true, new ScanProgress(), TestContext.CancellationToken);
        await using (var db = await CreateDbAsync())
            Assert.AreEqual(1, (await DurationAsync(db, "Clips/clip.mp4"))!.Value, 0.05);
        Assert.HasCount(1, await SearchAsync("clip d:1s"));
    }

    [TestMethod]
    public async Task ScansReadTheDurationOfAudioWithFfmpeg()
    {
        await using var services = await CoreServices.CreateAsync(_library, s =>
            s["Sfe:Thumbnails:FfmpegPath"] = Environment.GetEnvironmentVariable("SFE_TEST_FFMPEG") is { Length: > 0 } path ? path : "ffmpeg");
        var ffmpeg = services.Get<Ffmpeg>();
        if (!ffmpeg.IsAvailable)
            Assert.Inconclusive("ffmpeg not found: set SFE_TEST_FFMPEG to run the audio duration test.");

        var song = _library.FullPath("Music/song.wav");
        Directory.CreateDirectory(Path.GetDirectoryName(song)!);
        var (exitCode, errors) = await ffmpeg.RunAsync(
            ["-hide_banner", "-loglevel", "error", "-nostdin", "-y", "-f", "lavfi", "-i", "sine=frequency=440:duration=2", song],
            TimeSpan.FromSeconds(30), cancellationToken: TestContext.CancellationToken);
        Assert.AreEqual(0, exitCode, errors);

        async Task ScanMusicAsync() =>
            await services.Get<DirectoryIndexer>().IndexAsync(_library.FullPath("Music"), true, new ScanProgress(), TestContext.CancellationToken);

        await ScanMusicAsync();
        await using (var db = await CreateDbAsync())
        {
            Assert.AreEqual((null, null), await SizeAsync(db, "Music/song.wav"), "Audio has no resolution.");
            Assert.AreEqual(2, (await DurationAsync(db, "Music/song.wav"))!.Value, 0.05);

            // An audio file indexed by a version that did not read durations: the next scan reads it.
            var fullName = _library.FullPath("Music/song.wav");
            await db.Files.Where(f => f.FullName == fullName).ExecuteUpdateAsync(s => s.SetProperty(f => f.Duration, (double?)null), TestContext.CancellationToken);
        }

        await ScanMusicAsync();
        await using (var db = await CreateDbAsync())
            Assert.AreEqual(2, (await DurationAsync(db, "Music/song.wav"))!.Value, 0.05);

        Assert.HasCount(1, await SearchAsync("song d:2s"));
        Assert.HasCount(1, await SearchAsync("song -d:>1m"));
        Assert.IsEmpty(await SearchAsync("song w:>0"));
        Assert.IsEmpty(await SearchAsync("song r:lr"));
        Assert.AreEqual(2, (await SearchAsync("song")).Single().Duration!.Value, 0.05);

        var listing = services.Get<LibraryBrowser>().List("Music", 0, 10)!;
        var listed = await services.Get<TagService>().AttachIndexDataAsync(listing.Items, TestContext.CancellationToken);
        Assert.AreEqual(2, listed.Single().Duration!.Value, 0.05);
        Assert.IsNull(listed.Single().Width);
    }

    [TestMethod]
    public void AudioIsReadOnlyWithFfmpeg()
    {
        var dimensions = _services.Get<MediaDimensions>();
        Assert.AreEqual(_services.Get<Ffmpeg>().IsAvailable, dimensions.CanRead(ItemKind.Audio));
        Assert.IsTrue(dimensions.CanRead(ItemKind.Image));
        Assert.IsFalse(dimensions.CanRead(ItemKind.File));
    }

    [TestMethod]
    public async Task UnreadableVideosAreNotReadAgainForTheirDuration()
    {
        if (!_services.Get<Ffmpeg>().IsAvailable)
            Assert.Inconclusive("ffmpeg not found: the fixture's videos are not read without it.");

        // The fixture's videos are text: ffmpeg finds no picture nor length in them, and that is remembered.
        await ScanAsync();
        await using var db = await CreateDbAsync();
        Assert.AreEqual((0, 0), await SizeAsync(db, "Media/Episode 2.mkv"));
        Assert.AreEqual<double?>(0, await DurationAsync(db, "Media/Episode 2.mkv"));
        Assert.IsNull(await DurationAsync(db, "RFile1.txt"));
    }

    /* ---------- Helpers ---------- */

    private static PixelSize? Header(byte[] bytes) => MediaDimensions.ReadImageHeader(new MemoryStream(bytes));

    /// <summary>Signature and IHDR chunk: all a reader needs.</summary>
    private static byte[] Png(int width, int height)
    {
        var png = new byte[33];
        new byte[] { 0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A, 0, 0, 0, 13, (byte)'I', (byte)'H', (byte)'D', (byte)'R' }.CopyTo(png, 0);
        BinaryPrimitives.WriteInt32BigEndian(png.AsSpan(16), width);
        BinaryPrimitives.WriteInt32BigEndian(png.AsSpan(20), height);
        return png;
    }

    private static byte[] WebP(string chunk)
    {
        var webp = new byte[40];
        "RIFF"u8.CopyTo(webp);
        "WEBP"u8.CopyTo(webp.AsSpan(8));
        System.Text.Encoding.ASCII.GetBytes(chunk).CopyTo(webp, 12);
        return webp;
    }

    private static void WriteUInt24(byte[] buffer, int at, int value)
    {
        buffer[at] = (byte)value;
        buffer[at + 1] = (byte)(value >> 8);
        buffer[at + 2] = (byte)(value >> 16);
    }

    /// <summary>SOI, an APP0, an EXIF APP1 with the orientation (0 = none), the frame header (SOF0) and EOI.</summary>
    private static byte[] Jpeg(int width, int height, int orientation = 0, bool bigEndian = false)
    {
        var bytes = new List<byte> { 0xFF, 0xD8, 0xFF, 0xE0, 0x00, 0x07, (byte)'J', (byte)'F', (byte)'I', (byte)'F', 0 };
        if (orientation > 0)
        {
            // TIFF header + IFD0 with one entry: tag 0x0112, type SHORT, count 1, value.
            var tiff = new byte[26];
            void Write16(int at, int value)
            {
                if (bigEndian)
                    BinaryPrimitives.WriteUInt16BigEndian(tiff.AsSpan(at), (ushort)value);
                else
                    BinaryPrimitives.WriteUInt16LittleEndian(tiff.AsSpan(at), (ushort)value);
            }

            void Write32(int at, int value)
            {
                if (bigEndian)
                    BinaryPrimitives.WriteInt32BigEndian(tiff.AsSpan(at), value);
                else
                    BinaryPrimitives.WriteInt32LittleEndian(tiff.AsSpan(at), value);
            }

            tiff[0] = tiff[1] = (byte)(bigEndian ? 'M' : 'I');
            Write16(2, 42);
            Write32(4, 8);          // IFD0 right after the header
            Write16(8, 1);          // one entry
            Write16(10, 0x0112);
            Write16(12, 3);         // SHORT
            Write32(14, 1);
            Write16(18, orientation);
            byte[] app1 = [.. "Exif\0\0"u8, .. tiff];
            bytes.AddRange([0xFF, 0xE1, (byte)((app1.Length + 2) >> 8), (byte)(app1.Length + 2)]);
            bytes.AddRange(app1);
        }

        bytes.AddRange([0xFF, 0xC0, 0x00, 0x11, 8, (byte)(height >> 8), (byte)height, (byte)(width >> 8), (byte)width, 3]);
        bytes.AddRange(new byte[9]);
        bytes.AddRange([0xFF, 0xD9]);
        return [.. bytes];
    }

    private async Task<string> WriteAsync(string relative, byte[] content)
    {
        var path = _library.FullPath(relative);
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        await File.WriteAllBytesAsync(path, content, TestContext.CancellationToken);
        return path;
    }

    private async Task ScanAsync()
    {
        var progress = new ScanProgress();
        await _services.Get<DirectoryIndexer>().IndexAsync(_library.Root, true, progress, TestContext.CancellationToken);
        Assert.AreEqual(0, progress.Errors);
    }

    private async Task<IReadOnlyList<LibraryItem>> SearchAsync(string query) =>
        (await _services.Get<FileSearchService>().SearchAsync(query, "", null, 100, cancellationToken: TestContext.CancellationToken))!.Items;

    private async Task<(int?, int?)> SizeAsync(SfeDbContext db, string relative)
    {
        var fullName = _library.FullPath(relative);
        var file = await db.Files.SingleAsync(f => f.FullName == fullName, TestContext.CancellationToken);
        return (file.Width, file.Height);
    }

    private async Task<double?> DurationAsync(SfeDbContext db, string relative)
    {
        var fullName = _library.FullPath(relative);
        return await db.Files.Where(f => f.FullName == fullName).Select(f => f.Duration).SingleAsync(TestContext.CancellationToken);
    }

    private Task<SfeDbContext> CreateDbAsync() =>
        _services.Get<IDbContextFactory<SfeDbContext>>().CreateDbContextAsync(TestContext.CancellationToken);
}
