using System.Buffers.Binary;
using Sfe.Core.Library;
using Sfe.Core.Thumbnails;
using Sfe.Tests.Infrastructure;

namespace Sfe.Tests;

/// <summary>Animated pictures told from still ones by their structure (the listings' hover on large animated images).</summary>
[TestClass]
public sealed class ImageAnimationTests
{
    public TestContext TestContext { get; set; } = null!;

    [TestMethod]
    public void GifsWithMoreThanOneFrameAreAnimated()
    {
        Assert.IsTrue(IsAnimated(Gif(frames: 2)));
        Assert.IsTrue(IsAnimated(Gif(frames: 3, globalColors: false, localColors: true)));
        Assert.IsFalse(IsAnimated(Gif(frames: 1)));
        // A loop extension alone doesn't make it move.
        Assert.IsFalse(IsAnimated(Gif(frames: 1, loop: true)));
        Assert.IsTrue(IsAnimated(Gif(frames: 2, loop: true)));
    }

    [TestMethod]
    public void BrokenGifsAreStill()
    {
        var gif = Gif(frames: 2);
        Assert.IsFalse(IsAnimated(gif[..30]));
        Assert.IsFalse(IsAnimated(gif[..13]));
    }

    [TestMethod]
    public void PngsWithAnAnimationControlChunkOfSeveralFramesAreAnimated()
    {
        Assert.IsTrue(IsAnimated(Png(animationFrames: 4)));
        Assert.IsFalse(IsAnimated(Png(animationFrames: 1)));
        Assert.IsFalse(IsAnimated(Png(animationFrames: null)));
    }

    [TestMethod]
    public void WebPsWithTheAnimationFlagAreAnimated()
    {
        Assert.IsTrue(IsAnimated(WebP("VP8X", 0x02)));
        Assert.IsTrue(IsAnimated(WebP("VP8X", 0x12)));
        Assert.IsFalse(IsAnimated(WebP("VP8X", 0x10)));
        Assert.IsFalse(IsAnimated(WebP("VP8L", 0x02)));
    }

    [TestMethod]
    public void AvifImageSequencesAreAnimated()
    {
        Assert.IsTrue(IsAnimated(Ftyp("avis", "avif", "mif1")));
        Assert.IsTrue(IsAnimated(Ftyp("avif", "avis", "msf1")));
        Assert.IsFalse(IsAnimated(Ftyp("avif", "avif", "mif1")));
        Assert.IsFalse(IsAnimated(Ftyp("isom", "mp41")));
    }

    [TestMethod]
    public void OtherFilesAreStill()
    {
        Assert.IsFalse(IsAnimated([0xFF, 0xD8, 0xFF, 0xE0, 0, 16]));
        Assert.IsFalse(IsAnimated("plain text, long enough to read a header from"u8.ToArray()));
        Assert.IsFalse(IsAnimated([]));
    }

    [TestMethod]
    public void PicturesMadeByFfmpegAreToldApart()
    {
        static bool Sample(string name)
        {
            using var stream = File.OpenRead(TestSamples.PathOf(name));
            return ImageAnimation.IsAnimated(stream);
        }

        Assert.IsTrue(Sample("testsrc-64x48-5frames.gif"));
        Assert.IsFalse(Sample("testsrc-64x48-still.gif"));
        Assert.IsTrue(Sample("testsrc-64x48-5frames.apng"));
        Assert.IsFalse(Sample("testsrc-64x48-still.png"));
    }

    /* ---------- Helpers ---------- */

    private static bool IsAnimated(byte[] bytes) => ImageAnimation.IsAnimated(new MemoryStream(bytes));

    /// <summary>A 2×2 GIF of <paramref name="frames"/> frames, each with a graphic control extension.</summary>
    internal static byte[] Gif(int frames, bool globalColors = true, bool localColors = false, bool loop = false)
    {
        var gif = new List<byte>();
        gif.AddRange("GIF89a"u8.ToArray());
        gif.AddRange([2, 0, 2, 0, (byte)(globalColors ? 0x80 : 0), 0, 0]);    // 2 colours when present
        if (globalColors)
            gif.AddRange([0, 0, 0, 255, 255, 255]);
        if (loop)
        {
            gif.AddRange([0x21, 0xFF, 11]);
            gif.AddRange("NETSCAPE2.0"u8.ToArray());
            gif.AddRange([3, 1, 0, 0, 0]);
        }

        for (var i = 0; i < frames; i++)
        {
            gif.AddRange([0x21, 0xF9, 4, 0, 10, 0, 0, 0]);
            gif.AddRange([0x2C, 0, 0, 0, 0, 2, 0, 2, 0, (byte)(localColors ? 0x80 : 0)]);
            if (localColors)
                gif.AddRange([0, 0, 0, 255, 255, 255]);
            gif.AddRange([2, 2, 0x44, 0x01, 0]);    // LZW code size, one sub-block, terminator
        }

        gif.Add(0x3B);
        return [.. gif];
    }

    /// <summary>A PNG with an <c>acTL</c> chunk of <paramref name="animationFrames"/> frames (none when null) before IDAT.</summary>
    private static byte[] Png(int? animationFrames)
    {
        var png = new List<byte>([0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A]);
        void Chunk(string type, byte[] data)
        {
            var length = new byte[4];
            BinaryPrimitives.WriteInt32BigEndian(length, data.Length);
            png.AddRange(length);
            png.AddRange(System.Text.Encoding.ASCII.GetBytes(type));
            png.AddRange(data);
            png.AddRange(new byte[4]);      // CRC, not checked
        }

        Chunk("IHDR", [0, 0, 0, 2, 0, 0, 0, 2, 8, 6, 0, 0, 0]);
        Chunk("tEXt", "Comment\0x"u8.ToArray());
        if (animationFrames is { } frames)
            Chunk("acTL", [0, 0, 0, (byte)frames, 0, 0, 0, 0]);
        Chunk("IDAT", [1, 2, 3]);
        Chunk("IEND", []);
        return [.. png];
    }

    private static byte[] WebP(string chunk, byte flags)
    {
        var webp = new byte[30];
        "RIFF"u8.CopyTo(webp);
        "WEBP"u8.CopyTo(webp.AsSpan(8));
        System.Text.Encoding.ASCII.GetBytes(chunk).CopyTo(webp, 12);
        webp[16] = 10;
        webp[20] = flags;
        return webp;
    }

    private static byte[] Ftyp(string major, params string[] compatible)
    {
        var size = 16 + 4 * compatible.Length;
        var box = new byte[size + 8];
        BinaryPrimitives.WriteInt32BigEndian(box, size);
        "ftyp"u8.CopyTo(box.AsSpan(4));
        System.Text.Encoding.ASCII.GetBytes(major).CopyTo(box, 8);
        for (var i = 0; i < compatible.Length; i++)
            System.Text.Encoding.ASCII.GetBytes(compatible[i]).CopyTo(box, 16 + 4 * i);
        return box;
    }
}
