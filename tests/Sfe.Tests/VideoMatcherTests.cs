using Sfe.Core.FileTracking;
using Sfe.Core.Trickplay;

namespace Sfe.Tests;

/// <summary>Video fingerprints and their matching, on made-up frame hashes (see <see cref="SimilarVideoTests"/> for real videos).</summary>
[TestClass]
public sealed class VideoMatcherTests
{
    public TestContext TestContext { get; set; } = null!;

    [TestMethod]
    public void ReencodedCopyMatchesAndUnrelatedVideoDoesNot()
    {
        var original = RandomVideo(60, seed: 1);
        var copy = Blur(original, bits: 3, seed: 2);
        var other = RandomVideo(60, seed: 3);

        var groups = VideoMatcher.FindGroups([original, copy, other], [100, 200, 100], 80, TestContext.CancellationToken);

        var group = groups.Single();
        // Same length: the bigger file is the reference.
        Assert.AreEqual(new VideoMember(1, null), group.Members[0]);
        Assert.AreEqual(new VideoMember(0, 100), group.Members[1]);
    }

    [TestMethod]
    public void TrimmedCopyMatchesAtItsOffset()
    {
        var original = RandomVideo(60, seed: 1);
        var trimmed = Slice(Blur(original, bits: 2, seed: 2), 6, 54);

        var group = VideoMatcher.FindGroups([trimmed, original], [100, 100], 80, TestContext.CancellationToken).Single();

        // The longer one is the reference; 54 of its 60 frames are in the copy.
        Assert.AreEqual(new VideoMember(1, null), group.Members[0]);
        Assert.AreEqual(new VideoMember(0, 90), group.Members[1]);
    }

    [TestMethod]
    public void ExcerptMatchesOnlyItsShareOfTheLongerVideo()
    {
        var original = RandomVideo(60, seed: 1);
        var excerpt = Slice(original, 30, 20);

        Assert.IsEmpty(VideoMatcher.FindGroups([original, excerpt], [100, 10], 80, TestContext.CancellationToken));
        var group = VideoMatcher.FindGroups([original, excerpt], [100, 10], 30, TestContext.CancellationToken).Single();
        Assert.AreEqual(new VideoMember(1, 33), group.Members[1]);
    }

    [TestMethod]
    public void VideoLinkedThroughAnotherIsScoredAgainstTheReference()
    {
        // The middle one shares 40 frames with each end; the ends share 20 with each other, under the minimum.
        var a = RandomVideo(60, seed: 1);
        var c = Concat(Slice(a, 40, 20), RandomVideo(40, seed: 2));
        var b = Concat(Slice(a, 20, 40), Slice(c, 20, 20));

        var group = VideoMatcher.FindGroups([a, b, c], [3, 2, 1], 60, TestContext.CancellationToken).Single();

        Assert.AreEqual(new VideoMember(0, null), group.Members[0]);
        CollectionAssert.AreEqual(new[] { new VideoMember(1, 66), new VideoMember(2, 33) }, group.Members.Skip(1).ToArray());
    }

    [TestMethod]
    public void FlatFramesNeverMatch()
    {
        // Two black videos hash alike, but show nothing.
        var black = new VideoFingerprint(10, new ulong[30], new byte[30]);
        var otherBlack = new VideoFingerprint(10, new ulong[40], new byte[40]);
        Assert.IsEmpty(VideoMatcher.FindGroups([black, otherBlack], [1, 1], 50, TestContext.CancellationToken));

        // A copy with a black intro: only the frames with a picture count.
        var original = RandomVideo(40, seed: 1);
        var withIntro = Concat(new VideoFingerprint(10, new ulong[10], new byte[10]), original);
        var group = VideoMatcher.FindGroups([original, withIntro], [1, 2], 80, TestContext.CancellationToken).Single();
        Assert.AreEqual(100, group.Members[1].Percent);
    }

    [TestMethod]
    public void FingerprintFileRoundTrips()
    {
        var path = Path.Combine(Path.GetTempPath(), $"sfe-fingerprint-{Guid.NewGuid():N}.bin");
        try
        {
            var fingerprint = RandomVideo(5, seed: 4);
            fingerprint.Write(path);
            var read = VideoFingerprint.Read(path, 7)!;
            Assert.AreEqual(7, read.Interval);
            CollectionAssert.AreEqual(fingerprint.Hashes, read.Hashes);
            CollectionAssert.AreEqual(fingerprint.Contrast, read.Contrast);

            File.WriteAllBytes(path, [1, 2, 3, 4, 5]);
            Assert.IsNull(VideoFingerprint.Read(path, 7));
        }
        finally
        {
            File.Delete(path);
        }
    }

    [TestMethod]
    public void FrameHashComparesNeighbourPixels()
    {
        // Brightness falling to the right on every row: each pixel is brighter than its right neighbour.
        var pixels = new byte[VideoFingerprint.FrameBytes];
        for (var i = 0; i < pixels.Length; i++)
            pixels[i] = (byte)(200 - i % VideoFingerprint.PixelWidth * 20);
        var falling = VideoFingerprint.FromGreyFrames(1, pixels, 1);
        Assert.AreEqual(ulong.MaxValue, falling.Hashes[0]);
        Assert.AreEqual(160, falling.Contrast[0]);
        Assert.IsTrue(falling.IsInformative(0));

        var flat = VideoFingerprint.FromGreyFrames(1, new byte[VideoFingerprint.FrameBytes], 1);
        Assert.AreEqual(0UL, flat.Hashes[0]);
        Assert.IsFalse(flat.IsInformative(0));
    }

    private static VideoFingerprint RandomVideo(int frames, int seed)
    {
        var random = new Random(seed);
        var hashes = new ulong[frames];
        for (var i = 0; i < frames; i++)
            hashes[i] = (ulong)random.NextInt64() ^ ((ulong)random.Next(2) << 63);
        return new VideoFingerprint(10, hashes, Enumerable.Repeat((byte)100, frames).ToArray());
    }

    /// <summary>The same video with <paramref name="bits"/> random bits of each frame flipped, like a re-encode.</summary>
    private static VideoFingerprint Blur(VideoFingerprint video, int bits, int seed)
    {
        var random = new Random(seed);
        var hashes = video.Hashes.Select(h =>
        {
            for (var b = 0; b < bits; b++)
                h ^= 1UL << random.Next(64);
            return h;
        }).ToArray();
        return new VideoFingerprint(video.Interval, hashes, [.. video.Contrast]);
    }

    private static VideoFingerprint Slice(VideoFingerprint video, int start, int count) =>
        new(video.Interval, video.Hashes[start..(start + count)], video.Contrast[start..(start + count)]);

    private static VideoFingerprint Concat(VideoFingerprint first, VideoFingerprint second) =>
        new(first.Interval, [.. first.Hashes, .. second.Hashes], [.. first.Contrast, .. second.Contrast]);
}
