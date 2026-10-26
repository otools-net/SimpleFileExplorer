using System.Buffers.Binary;

namespace Sfe.Core.Files.Compression;

/// <summary>
/// How large a 7z's list of entries (its header) is, read before SharpCompress opens it: SharpCompress holds that list
/// whole, with an object per entry (~570 bytes each: 1 000 000 entries took 717 MB), so a list past
/// <c>Files:Archives:MaxSevenZipListMegabytes</c> is refused rather than read (agents/docs/features/archive-reading.md).
/// </summary>
public static class SevenZipList
{
    /// <summary>The default limit (<c>MaxSevenZipListMegabytes</c>): about 400 000 entries of usual names, ~500 MB held.</summary>
    public const int DefaultMaxMegabytes = 32;

    private const byte EncodedHeader = 0x17;
    private const byte PackInfo = 0x06;
    private const byte UnpackInfo = 0x07;
    private const byte Folder = 0x0B;
    private const byte CodersUnpackSize = 0x0C;
    private const byte Size = 0x09;
    private const byte Crc = 0x0A;
    private const byte End = 0x00;

    /// <summary>
    /// The size of the list once unpacked (its declared size when the list is itself compressed, as 7-Zip writes it),
    /// or null when the archive's headers can't be followed (SharpCompress then says what is wrong). The stream is
    /// left where it was.
    /// </summary>
    public static long? UnpackedSize(Stream stream)
    {
        var position = stream.Position;
        try
        {
            Span<byte> start = stackalloc byte[32];
            stream.Position = 0;
            if (stream.ReadAtLeast(start, 32, throwOnEndOfStream: false) < 32)
                return null;
            var offset = BinaryPrimitives.ReadInt64LittleEndian(start[12..]);
            var size = BinaryPrimitives.ReadInt64LittleEndian(start[20..]);
            if (offset < 0 || size <= 0 || 32 + offset + size > stream.Length)
                return null;
            stream.Position = 32 + offset;
            var first = stream.ReadByte();
            if (first != EncodedHeader)
                return size;
            // A compressed list: only its streams' description is read (a few bytes), never the list.
            var description = new byte[(int)Math.Min(size - 1, 64 * 1024)];
            stream.ReadExactly(description);
            return EncodedSize(new Reader(description));
        }
        catch (Exception ex) when (ex is EndOfStreamException or IndexOutOfRangeException or ArgumentOutOfRangeException or OverflowException or IOException)
        {
            return null;
        }
        finally
        {
            stream.Position = position;
        }
    }

    /// <summary>Throws when the list of the 7z in <paramref name="stream"/> is larger than <paramref name="maxBytes"/>.</summary>
    public static void Check(Stream stream, long maxBytes)
    {
        if (UnpackedSize(stream) is { } size && size > maxBytes)
            throw new ArchiveException(
                $"This 7z's list of entries is too large to be read by the server ({size / (1024.0 * 1024):N0} MB, at most {maxBytes / (1024 * 1024):N0} MB): " +
                "download it and open it on your computer, or ask an administrator to raise Files:Archives:MaxSevenZipListMegabytes.");
    }

    /// <summary>The largest unpack size of the streams describing a compressed list (its output: the list).</summary>
    private static long? EncodedSize(Reader reader)
    {
        long? largest = null;
        for (var id = reader.Byte(); id != End; id = reader.Byte())
        {
            switch (id)
            {
                case PackInfo:
                    {
                        reader.Number();
                        var packStreams = reader.Number();
                        for (var part = reader.Byte(); part != End; part = reader.Byte())
                        {
                            if (part == Size)
                                for (var i = 0L; i < packStreams; i++)
                                    reader.Number();
                            else if (part == Crc)
                                reader.Digests(packStreams);
                            else
                                return null;
                        }
                        break;
                    }
                case UnpackInfo:
                    {
                        if (reader.Byte() != Folder)
                            return null;
                        var folders = reader.Number();
                        if (reader.Byte() != 0 || folders is < 1 or > 64)
                            return null;
                        var outputs = new long[folders];
                        for (var f = 0; f < folders; f++)
                            outputs[f] = reader.FolderOutputs();
                        if (reader.Byte() != CodersUnpackSize)
                            return null;
                        foreach (var count in outputs)
                            for (var i = 0L; i < count; i++)
                                largest = Math.Max(largest ?? 0, reader.Number());
                        // The CRCs and the end follow: not needed.
                        return largest;
                    }
                default:
                    return null;
            }
        }
        return largest;
    }

    private sealed class Reader(byte[] data)
    {
        private int _at;

        public byte Byte() => data[_at++];

        /// <summary>7z's variable-length number: the first byte's high bits say how many bytes follow.</summary>
        public long Number()
        {
            var first = Byte();
            var mask = 0x80;
            long value = 0;
            for (var i = 0; i < 8; i++)
            {
                if ((first & mask) == 0)
                {
                    var high = first & (mask - 1);
                    value |= (long)high << (8 * i);
                    return value < 0 ? throw new OverflowException() : value;
                }
                value |= (long)Byte() << (8 * i);
                mask >>= 1;
            }
            return value < 0 ? throw new OverflowException() : value;
        }

        public void Skip(long count)
        {
            if (count < 0 || _at + count > data.Length)
                throw new EndOfStreamException();
            _at += (int)count;
        }

        public void Digests(long count)
        {
            var all = Byte();
            var defined = count;
            if (all == 0)
            {
                defined = 0;
                var bytes = (count + 7) / 8;
                for (var i = 0L; i < bytes; i++)
                    defined += System.Numerics.BitOperations.PopCount(Byte());
            }
            Skip(defined * 4);
        }

        /// <summary>Reads a folder's coders: how many outputs (unpack sizes) it has.</summary>
        public long FolderOutputs()
        {
            var coders = Number();
            if (coders is < 1 or > 64)
                throw new OverflowException();
            long inputs = 0, outputs = 0;
            for (var c = 0L; c < coders; c++)
            {
                var flags = Byte();
                Skip(flags & 0x0F);
                if ((flags & 0x10) != 0)
                {
                    inputs += Number();
                    outputs += Number();
                }
                else
                {
                    inputs++;
                    outputs++;
                }
                if ((flags & 0x20) != 0)
                    Skip(Number());
            }
            if (outputs is < 1 or > 64 || inputs > 64)
                throw new OverflowException();
            for (var pair = 0L; pair < outputs - 1; pair++)
            {
                Number();
                Number();
            }
            var packed = inputs - (outputs - 1);
            if (packed > 1)
                for (var i = 0L; i < packed; i++)
                    Number();
            return outputs;
        }
    }
}
