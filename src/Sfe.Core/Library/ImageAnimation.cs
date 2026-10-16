using System.Buffers.Binary;

namespace Sfe.Core.Library;

/// <summary>
/// Tells animated pictures (GIF, APNG, WebP, AVIF sequences) from still ones by their structure, without decoding:
/// the listings show large images through a still thumbnail and play the original on hover only when it moves.
/// </summary>
public static class ImageAnimation
{
    /// <summary>
    /// Whether the picture has more than one frame. Reads the first bytes of PNG, WebP and AVIF files, and a GIF up to
    /// its second frame; any other content is still.
    /// </summary>
    public static bool IsAnimated(Stream stream)
    {
        Span<byte> head = stackalloc byte[32];
        var read = stream.ReadAtLeast(head, head.Length, throwOnEndOfStream: false);
        head = head[..read];

        if (head.Length >= 13 && (head[..6].SequenceEqual("GIF89a"u8) || head[..6].SequenceEqual("GIF87a"u8)))
            return IsAnimatedGif(stream, head[10]);
        if (head.Length >= 8 && head[..8].SequenceEqual<byte>([0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A]))
            return IsAnimatedPng(stream);
        if (head.Length >= 21 && head[..4].SequenceEqual("RIFF"u8) && head[8..12].SequenceEqual("WEBP"u8))
            return head[12..16].SequenceEqual("VP8X"u8) && (head[20] & 0x02) != 0;     // the VP8X animation flag
        if (head.Length >= 12 && head[4..8].SequenceEqual("ftyp"u8))
            return IsImageSequence(stream, BinaryPrimitives.ReadUInt32BigEndian(head));
        return false;
    }

    /// <summary>Counts the image descriptors (after the global colour table) up to the second one or the trailer.</summary>
    private static bool IsAnimatedGif(Stream stream, byte screenFlags)
    {
        stream.Position = 13 + ColorTableSize(screenFlags);
        var frames = 0;
        Span<byte> descriptor = stackalloc byte[9];
        while (true)
        {
            switch (stream.ReadByte())
            {
                case 0x21:      // extension: label, then data sub-blocks
                    if (stream.ReadByte() < 0 || !SkipSubBlocks(stream))
                        return false;
                    break;
                case 0x2C:      // image: descriptor, local colour table, LZW code size, data sub-blocks
                    if (++frames > 1)
                        return true;
                    if (stream.ReadAtLeast(descriptor, 9, throwOnEndOfStream: false) < 9)
                        return false;
                    stream.Position += ColorTableSize(descriptor[8]) + 1;
                    if (!SkipSubBlocks(stream))
                        return false;
                    break;
                default:        // trailer (0x3B), end of file or garbage
                    return false;
            }
        }
    }

    private static int ColorTableSize(byte flags) => (flags & 0x80) != 0 ? 3 << ((flags & 0x07) + 1) : 0;

    /// <returns>False at the end of the file.</returns>
    private static bool SkipSubBlocks(Stream stream)
    {
        while (true)
        {
            var size = stream.ReadByte();
            if (size <= 0)
                return size == 0;
            stream.Position += size;
            if (stream.Position > stream.Length)
                return false;
        }
    }

    /// <summary>An APNG has an <c>acTL</c> chunk with more than one frame before its image data.</summary>
    private static bool IsAnimatedPng(Stream stream)
    {
        stream.Position = 8;
        Span<byte> chunk = stackalloc byte[12];
        while (stream.ReadAtLeast(chunk, 12, throwOnEndOfStream: false) == 12)
        {
            var length = BinaryPrimitives.ReadUInt32BigEndian(chunk);
            var type = chunk[4..8];
            if (type.SequenceEqual("acTL"u8))
                return BinaryPrimitives.ReadUInt32BigEndian(chunk[8..]) > 1;    // num_frames
            if (type.SequenceEqual("IDAT"u8) || type.SequenceEqual("IEND"u8))
                return false;
            // Next chunk: the rest of this one's data (4 bytes of it were read) and its 4-byte CRC.
            stream.Position += length;
            if (stream.Position > stream.Length)
                return false;
        }

        return false;
    }

    /// <summary>An AVIF sequence names the <c>avis</c> brand in its <c>ftyp</c> box (major or compatible).</summary>
    private static bool IsImageSequence(Stream stream, uint boxSize)
    {
        if (boxSize is < 16 or > 4096)
            return false;
        var box = new byte[boxSize];
        stream.Position = 0;
        if (stream.ReadAtLeast(box, box.Length, throwOnEndOfStream: false) < box.Length)
            return false;
        for (var at = 8; at + 4 <= box.Length; at += 4)
        {
            if (at != 12 && box.AsSpan(at, 4).SequenceEqual("avis"u8))     // 12: the minor version
                return true;
        }

        return false;
    }
}
