using System.Buffers.Binary;
using System.Numerics;

namespace Sfe.Core.Trickplay;

/// <summary>
/// Perceptual fingerprint of a video: a 64-bit difference hash (dHash) of each trickplay frame. Copies of a video at
/// another resolution, bitrate or codec have other bytes (and fingerprints, see <see cref="Indexing.FileFingerprint"/>)
/// but nearly the same frame hashes, so they can be recognised (<see cref="FileTracking.SimilarVideoFinder"/>).
/// </summary>
/// <remarks>
/// A frame is shrunk to 9×8 grey pixels; each bit says whether a pixel is brighter than its right neighbour.
/// Measured on a real 720p video: re-encodes at 360p/480p differ by 0.2 bits per frame on average, a copy trimmed by
/// 3 s by 3 bits, frames of an unrelated part of the same recording by about 20. Flat frames (black, fades) hash
/// alike whatever the video: their <see cref="Contrast"/> lets the comparison leave them out.
/// </remarks>
public sealed class VideoFingerprint
{
    /// <summary>Size of the grey picture a frame is shrunk to.</summary>
    public const int PixelWidth = 9, PixelHeight = 8, FrameBytes = PixelWidth * PixelHeight;

    /// <summary>Frames whose darkest and brightest pixels are closer than this carry no usable picture.</summary>
    public const int MinimumContrast = 16;

    private const uint Magic = 0x31504653;      // "SFP1"
    private const int RecordBytes = sizeof(ulong) + 1;

    public VideoFingerprint(int interval, ulong[] hashes, byte[] contrast)
    {
        if (hashes.Length != contrast.Length)
            throw new ArgumentException("One contrast value per hash is needed.", nameof(contrast));
        Interval = interval;
        Hashes = hashes;
        Contrast = contrast;
    }

    /// <summary>Seconds between two frames (the trickplay interval): only fingerprints of the same interval compare.</summary>
    public int Interval { get; }

    public ulong[] Hashes { get; }

    /// <summary>Difference between the brightest and the darkest of the 9×8 pixels, per frame.</summary>
    public byte[] Contrast { get; }

    public int Count => Hashes.Length;

    public bool IsInformative(int frame) => Contrast[frame] >= MinimumContrast;

    /// <summary>Bits that differ between two frame hashes (0 to 64).</summary>
    public static int Distance(ulong a, ulong b) => BitOperations.PopCount(a ^ b);

    /// <summary>Fingerprint of <paramref name="count"/> frames given as consecutive 9×8 grey pictures (row by row).</summary>
    public static VideoFingerprint FromGreyFrames(int interval, ReadOnlySpan<byte> pixels, int count)
    {
        count = Math.Min(count, pixels.Length / FrameBytes);
        var hashes = new ulong[count];
        var contrast = new byte[count];
        for (var frame = 0; frame < count; frame++)
        {
            var picture = pixels.Slice(frame * FrameBytes, FrameBytes);
            ulong hash = 0;
            for (var y = 0; y < PixelHeight; y++)
            {
                for (var x = 0; x < PixelWidth - 1; x++)
                {
                    hash <<= 1;
                    if (picture[y * PixelWidth + x] > picture[y * PixelWidth + x + 1])
                        hash |= 1;
                }
            }

            hashes[frame] = hash;
            byte min = 255, max = 0;
            foreach (var value in picture)
            {
                min = Math.Min(min, value);
                max = Math.Max(max, value);
            }

            contrast[frame] = (byte)(max - min);
        }

        return new VideoFingerprint(interval, hashes, contrast);
    }

    /// <summary>Layout: <c>"SFP1"</c>, then per frame its hash (8 bytes, little-endian) and its contrast (1 byte).</summary>
    public void Write(string path)
    {
        var bytes = new byte[sizeof(uint) + Count * RecordBytes];
        BinaryPrimitives.WriteUInt32LittleEndian(bytes, Magic);
        for (var i = 0; i < Count; i++)
        {
            var record = bytes.AsSpan(sizeof(uint) + i * RecordBytes, RecordBytes);
            BinaryPrimitives.WriteUInt64LittleEndian(record, Hashes[i]);
            record[sizeof(ulong)] = Contrast[i];
        }

        var temp = path + ".tmp";
        File.WriteAllBytes(temp, bytes);
        File.Move(temp, path, overwrite: true);
    }

    /// <returns>null when the file is missing or not a fingerprint.</returns>
    public static VideoFingerprint? Read(string path, int interval)
    {
        byte[] bytes;
        try
        {
            bytes = File.ReadAllBytes(path);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return null;
        }

        if (bytes.Length < sizeof(uint) || BinaryPrimitives.ReadUInt32LittleEndian(bytes) != Magic
            || (bytes.Length - sizeof(uint)) % RecordBytes != 0)
            return null;

        var count = (bytes.Length - sizeof(uint)) / RecordBytes;
        var hashes = new ulong[count];
        var contrast = new byte[count];
        for (var i = 0; i < count; i++)
        {
            var record = bytes.AsSpan(sizeof(uint) + i * RecordBytes, RecordBytes);
            hashes[i] = BinaryPrimitives.ReadUInt64LittleEndian(record);
            contrast[i] = record[sizeof(ulong)];
        }

        return new VideoFingerprint(interval, hashes, contrast);
    }
}
