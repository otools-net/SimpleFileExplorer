using System.Runtime.InteropServices;
using Sfe.Core.Trickplay;

namespace Sfe.Core.FileTracking;

/// <summary>A video of a group and how much of it matches the group's reference (null for the reference itself).</summary>
internal readonly record struct VideoMember(int Video, int? Percent);

/// <summary>Similar videos: the reference (the longest) first.</summary>
internal sealed record VideoGroup(IReadOnlyList<VideoMember> Members);

/// <summary>
/// Finds videos with the same pictures among <see cref="VideoFingerprint"/>s of the same interval.
/// </summary>
/// <remarks>
/// <para>
/// Two videos match at an offset (frame <c>i</c> of one is frame <c>i + offset</c> of the other: a copy may be
/// trimmed) when their frames there are within <see cref="FrameDistance"/> bits. Their match percentage is the share
/// of matching frames out of the frames of the longer one (flat frames left out of both): a copy at another
/// resolution comes close to 100 %, a copy trimmed by a tenth to 90 %, a short excerpt of a long video stays low.
/// </para>
/// <para>
/// Comparing every pair is out of the question for a large library. Like audio fingerprinting, candidates come from
/// an index instead: each frame hash is cut in three bands, and frames with an equal band (certain under 3 differing
/// bits, likely up to ~6) vote for their offset. Only pairs with several votes are scored, at their most voted offsets.
/// A band value shared by more than <see cref="BucketLimit"/> frames (a logo, an intro shared by many videos) is
/// skipped: it says little and would cost a lot.
/// </para>
/// </remarks>
internal static class VideoMatcher
{
    /// <summary>
    /// Differing bits under which two frames are the same picture. Measured: 0–4 for re-encodes and trimmed copies,
    /// ~20 for unrelated frames, but frames of one recording are within 10 bits a third of the time: a match is decided
    /// on many frames at one offset, never on one frame.
    /// </summary>
    public const int FrameDistance = 10;

    /// <summary>Fewer matching frames say nothing (a clip shorter than a few intervals can't be matched).</summary>
    public const int MinimumMatchedFrames = 3;

    public const int BucketLimit = 64;
    private const int MinimumVotes = 2;
    private const int OffsetsTried = 3;
    private const int MaxFrames = 1 << 16;
    private const int MaxVideos = 1 << 24;
    private static readonly (int Shift, int Bits)[] Bands = [(0, 21), (21, 21), (42, 22)];

    /// <summary>
    /// Groups of videos matching at <paramref name="minimumPercent"/> % or more; a video matching any of a group is in
    /// it. Each member's percentage is against the group's reference: the video with the most picture, then the
    /// biggest file (likely the best quality).
    /// </summary>
    /// <param name="videos">Fingerprints of one interval.</param>
    /// <param name="sizes">File size of each video.</param>
    public static List<VideoGroup> FindGroups(IReadOnlyList<VideoFingerprint> videos, IReadOnlyList<long> sizes, int minimumPercent, CancellationToken cancellationToken)
    {
        var informative = videos.Select(InformativeFrames).ToArray();
        var pairs = FindPairs(videos, informative, minimumPercent, cancellationToken);

        var parents = Enumerable.Range(0, videos.Count).ToArray();
        int Find(int v)
        {
            while (parents[v] != v)
                v = parents[v] = parents[parents[v]];
            return v;
        }

        foreach (var key in pairs.Keys)
            parents[Find((int)(key >> 32))] = Find((int)key);

        var groups = new List<VideoGroup>();
        foreach (var members in Enumerable.Range(0, videos.Count).GroupBy(Find).Where(g => g.Count() > 1))
        {
            cancellationToken.ThrowIfCancellationRequested();
            var reference = members.OrderByDescending(v => informative[v]).ThenByDescending(v => sizes[v]).First();
            var others = members
                .Where(v => v != reference)
                .Select(v => new VideoMember(v, pairs.TryGetValue(PairKey(reference, v), out var percent)
                    ? percent
                    // Only linked through other members: scored against the reference at every offset.
                    : Compare(videos[reference], videos[v], informative[reference], informative[v], AllOffsets(videos[reference], videos[v])).Percent))
                .OrderByDescending(m => m.Percent);
            groups.Add(new VideoGroup([new VideoMember(reference, null), .. others]));
        }

        return groups;
    }

    /// <summary>Pairs (<see cref="PairKey"/>) matching at <paramref name="minimumPercent"/> % or more, with their percentage.</summary>
    private static Dictionary<long, int> FindPairs(IReadOnlyList<VideoFingerprint> videos, int[] informative, int minimumPercent, CancellationToken cancellationToken)
    {
        // One entry per band of each frame: band (2 bits) | band value (22) | video (24) | frame (16). Sorted, the
        // frames sharing a band value are neighbours, by video then frame.
        var entries = new List<ulong>();
        for (var v = 0; v < Math.Min(videos.Count, MaxVideos); v++)
        {
            var video = videos[v];
            ulong? previous = null;
            for (var f = 0; f < Math.Min(video.Count, MaxFrames); f++)
            {
                // Flat frames match anything; a still scene repeats one hash, its first frame is enough.
                var hash = video.Hashes[f];
                if (!video.IsInformative(f) || hash == previous)
                    continue;
                previous = hash;
                for (var b = 0; b < Bands.Length; b++)
                {
                    var (shift, bits) = Bands[b];
                    var value = (hash >> shift) & ((1UL << bits) - 1);
                    entries.Add(((ulong)(uint)b << 62) | (value << 40) | ((ulong)(uint)v << 16) | (uint)f);
                }
            }
        }

        var sorted = CollectionsMarshal.AsSpan(entries);
        sorted.Sort();

        var votes = new Dictionary<long, Dictionary<int, int>>();
        for (var start = 0; start < sorted.Length;)
        {
            var key = sorted[start] >> 40;
            var end = start + 1;
            while (end < sorted.Length && sorted[end] >> 40 == key)
                end++;

            if (end - start <= BucketLimit)
            {
                for (var x = start; x < end; x++)
                {
                    for (var y = x + 1; y < end; y++)
                    {
                        int a = Video(sorted[x]), b = Video(sorted[y]);
                        if (a == b)
                            continue;
                        ref var offsets = ref CollectionsMarshal.GetValueRefOrAddDefault(votes, PairKey(a, b), out _);
                        offsets ??= [];
                        CollectionsMarshal.GetValueRefOrAddDefault(offsets, Frame(sorted[y]) - Frame(sorted[x]), out _)++;
                    }
                }
            }

            start = end;
            if ((start & 0xFFFF) == 0)
                cancellationToken.ThrowIfCancellationRequested();
        }

        var pairs = new Dictionary<long, int>();
        foreach (var (pair, offsets) in votes)
        {
            if (offsets.Values.Sum() < MinimumVotes)
                continue;
            int a = (int)(pair >> 32), b = (int)pair;
            var best = Compare(videos[a], videos[b], informative[a], informative[b],
                offsets.OrderByDescending(o => o.Value).Take(OffsetsTried).Select(o => o.Key));
            if (best.Percent >= minimumPercent)
                pairs[pair] = best.Percent;
        }

        return pairs;
    }

    /// <summary>Best percentage of <paramref name="a"/> and <paramref name="b"/> over <paramref name="offsets"/>.</summary>
    private static (int Percent, int Offset) Compare(VideoFingerprint a, VideoFingerprint b, int informativeA, int informativeB, IEnumerable<int> offsets)
    {
        (int Percent, int Offset) best = (0, 0);
        var frames = Math.Max(informativeA, informativeB);
        if (frames == 0)
            return best;
        foreach (var offset in offsets)
        {
            var matched = MatchedFrames(a, b, offset);
            var percent = matched < MinimumMatchedFrames ? 0 : (int)(100L * matched / frames);
            if (percent > best.Percent)
                best = (percent, offset);
        }

        return best;
    }

    /// <summary>Frames <c>i</c> of <paramref name="a"/> showing the picture of frame <c>i + offset</c> of <paramref name="b"/>.</summary>
    private static int MatchedFrames(VideoFingerprint a, VideoFingerprint b, int offset)
    {
        var matched = 0;
        var end = Math.Min(a.Count, b.Count - offset);
        for (var i = Math.Max(0, -offset); i < end; i++)
        {
            var j = i + offset;
            if (a.IsInformative(i) && b.IsInformative(j) && VideoFingerprint.Distance(a.Hashes[i], b.Hashes[j]) <= FrameDistance)
                matched++;
        }

        return matched;
    }

    private static IEnumerable<int> AllOffsets(VideoFingerprint a, VideoFingerprint b) => Enumerable.Range(1 - a.Count, a.Count + b.Count - 1);

    private static int InformativeFrames(VideoFingerprint video)
    {
        var count = 0;
        for (var f = 0; f < video.Count; f++)
        {
            if (video.IsInformative(f))
                count++;
        }

        return count;
    }

    /// <summary>Key of an unordered pair of videos.</summary>
    private static long PairKey(int a, int b) => a < b ? ((long)a << 32) | (uint)b : ((long)b << 32) | (uint)a;

    private static int Video(ulong entry) => (int)((entry >> 16) & (MaxVideos - 1));

    private static int Frame(ulong entry) => (int)(entry & (MaxFrames - 1));
}
