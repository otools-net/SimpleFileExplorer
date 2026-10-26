using System.Buffers.Binary;
using System.IO.Compression;
using System.Text;

namespace Sfe.Core.Files.Compression;

/// <summary>
/// A zip's central directory, read record after record (never <see cref="ZipArchive"/>: it keeps every entry as an
/// object, gigabytes for millions of entries). Memory stays flat whatever the count; each entry keeps the offset of
/// its local header, so <see cref="OpenEntry"/> reaches it with one seek.
/// </summary>
internal sealed class ZipDirectory
{
    private const uint EndSignature = 0x06054b50;
    private const uint Zip64LocatorSignature = 0x07064b50;
    private const uint Zip64EndSignature = 0x06064b50;
    private const uint EntrySignature = 0x02014b50;
    private const uint LocalSignature = 0x04034b50;
    private const int EndLength = 22;
    private const int EntryLength = 46;
    private const int LocalLength = 30;

    private static readonly Encoding StrictUtf8 = new UTF8Encoding(false, throwOnInvalidBytes: true);
    private static readonly Encoding Dos = CodePage437();

    /// <summary>Where the directory starts in the file (any data prepended to the zip counted).</summary>
    public long Start { get; private init; }

    /// <summary>Where it ends: the (Zip64) end record.</summary>
    public long End { get; private init; }

    /// <summary>Bytes added before the zip (a self-extracting stub): every offset of the directory is shifted by it.</summary>
    public long Shift { get; private init; }

    /// <summary>The count the end record claims (may be wrong past 65 535 without Zip64: never relied on to read).</summary>
    public long DeclaredCount { get; private init; }

    /// <summary>Finds the end record (within the last 64 KB: its comment) and the directory it describes.</summary>
    public static ZipDirectory Locate(Stream stream)
    {
        var length = stream.Length;
        if (length < EndLength)
            throw new ArchiveException("The file is too short to be a zip.");
        var tailLength = (int)Math.Min(length, EndLength + ushort.MaxValue);
        var tail = new byte[tailLength];
        stream.Position = length - tailLength;
        stream.ReadExactly(tail);

        var at = -1;
        for (var i = tailLength - EndLength; i >= 0; i--)
            if (U32(tail, i) == EndSignature && i + EndLength + U16(tail, i + 20) <= tailLength)
            {
                at = i;
                break;
            }
        if (at < 0)
            throw new ArchiveException("The zip has no directory: it is broken or cut off.");

        var endPosition = length - tailLength + at;
        long disk = U16(tail, at + 4), directoryDisk = U16(tail, at + 6);
        long count = U16(tail, at + 10), size = U32(tail, at + 12), offset = U32(tail, at + 16);
        var directoryEnd = endPosition;

        if (endPosition >= 20 && ReadU32(stream, endPosition - 20) == Zip64LocatorSignature)
        {
            var locator = new byte[20];
            stream.Position = endPosition - 20;
            stream.ReadExactly(locator);
            var recordPosition = (long)U64(locator, 8);
            // Data prepended to the zip shifts this offset too: the record is usually right before the locator.
            if (recordPosition < 0 || recordPosition > length - 56 || ReadU32(stream, recordPosition) != Zip64EndSignature)
                recordPosition = endPosition - 20 - 56;
            if (recordPosition < 0 || ReadU32(stream, recordPosition) != Zip64EndSignature)
                throw new ArchiveException("The zip's Zip64 directory is missing: it is broken.");
            var record = new byte[56];
            stream.Position = recordPosition;
            stream.ReadExactly(record);
            disk = U32(record, 16);
            directoryDisk = U32(record, 20);
            count = (long)Math.Min(U64(record, 32), long.MaxValue);
            size = (long)Math.Min(U64(record, 40), long.MaxValue);
            offset = (long)Math.Min(U64(record, 48), long.MaxValue);
            directoryEnd = recordPosition;
        }

        if (disk != 0 || directoryDisk != 0)
            throw new ArchiveException("The zip is split into several files (.z01, .z02…): SFE reads only whole zips.");
        var start = directoryEnd - size;
        if (start < 0)
            throw new ArchiveException("The zip's directory claims to be larger than the file: it is broken.");
        var shift = start - offset;
        if (shift < 0)
            throw new ArchiveException("The zip's directory is not where it says: it is broken.");
        if (count > size / EntryLength)
            throw new ArchiveException("The zip's directory claims more entries than it can hold: it is broken.");
        return new ZipDirectory { Start = start, End = directoryEnd, Shift = shift, DeclaredCount = count };
    }

    /// <summary>The entries, as the directory lists them, read in one sequential pass.</summary>
    public IEnumerable<ArchiveEntry> Read(Stream stream, CancellationToken cancel)
    {
        var header = new byte[EntryLength];
        var name = new byte[ushort.MaxValue];
        var extra = new byte[ushort.MaxValue];
        stream.Position = Start;
        var position = Start;
        while (position < End)
        {
            cancel.ThrowIfCancellationRequested();
            if (End - position < EntryLength)
                throw new ArchiveException("The zip's directory is cut off: it is broken.");
            stream.ReadExactly(header);
            if (U32(header, 0) != EntrySignature)
                throw new ArchiveException("The zip's directory is damaged.");
            int madeBy = U16(header, 4), flags = U16(header, 8), method = U16(header, 10);
            int time = U16(header, 12), date = U16(header, 14);
            long packed = U32(header, 20), size = U32(header, 24);
            int nameLength = U16(header, 28), extraLength = U16(header, 30), commentLength = U16(header, 32);
            var attributes = U32(header, 38);
            long offset = U32(header, 42);
            position += EntryLength + nameLength + extraLength + commentLength;
            if (position > End)
                throw new ArchiveException("The zip's directory is cut off: it is broken.");
            stream.ReadExactly(name, 0, nameLength);
            stream.ReadExactly(extra, 0, extraLength);
            if (commentLength > 0)
                stream.Seek(commentLength, SeekOrigin.Current);

            DateTime? modified = DosTime(date, time);
            var at = 0;
            while (at + 4 <= extraLength)
            {
                int id = U16(extra, at), length = U16(extra, at + 2);
                var data = at + 4;
                if (data + length > extraLength)
                    break;
                if (id == 0x0001)
                {
                    // Zip64: the 8-byte values of the fields that are 0xFFFFFFFF, in this order.
                    var next = data;
                    if (size == uint.MaxValue && next + 8 <= data + length) { size = Long(extra, next); next += 8; }
                    if (packed == uint.MaxValue && next + 8 <= data + length) { packed = Long(extra, next); next += 8; }
                    if (offset == uint.MaxValue && next + 8 <= data + length) offset = Long(extra, next);
                }
                else if (id == 0x5455 && length >= 5 && (extra[data] & 1) != 0)
                {
                    // Extended timestamp: the modified time in UTC seconds, better than DOS' local 2-second time.
                    var seconds = BinaryPrimitives.ReadInt32LittleEndian(extra.AsSpan(data + 1));
                    modified = DateTime.SpecifyKind(DateTime.UnixEpoch.AddSeconds(seconds).ToLocalTime(), DateTimeKind.Unspecified);
                }
                at = data + length;
            }

            var text = DecodeName(name.AsSpan(0, nameLength), (flags & 0x0800) != 0);
            yield return new ArchiveEntry(
                text,
                KindOf(text, madeBy, attributes),
                size,
                packed,
                modified,
                Encrypted: (flags & 1) != 0,
                Locator: offset + Shift,
                Method: method);
        }
    }

    /// <summary>
    /// The data of <paramref name="entry"/>, decompressed and held to its declared size. Stored and deflated entries
    /// (nearly every zip); others answer that the method isn't read.
    /// </summary>
    public Stream OpenEntry(Stream stream, ArchiveEntry entry)
    {
        if (entry.Encrypted)
            throw new ArchiveException("This entry is protected by a password.", needsPassword: true);
        if (entry.Locator < 0 || entry.Locator > Start - LocalLength)
            throw new ArchiveException("An entry of the zip points outside of it: the archive is broken.");
        var local = new byte[LocalLength];
        stream.Position = entry.Locator;
        stream.ReadExactly(local);
        if (U32(local, 0) != LocalSignature)
            throw new ArchiveException("An entry of the zip is damaged.");
        var dataStart = entry.Locator + LocalLength + U16(local, 26) + U16(local, 28);
        // An entry's data never runs into the directory (overlapping entries are how some zip bombs multiply).
        if (entry.Packed < 0 || dataStart + entry.Packed > Start)
            throw new ArchiveException("An entry of the zip runs past its data: the archive is broken.");
        var packed = new WindowStream(stream, dataStart, entry.Packed);
        return entry.Method switch
        {
            0 when entry.Packed == entry.Size => new DeclaredSizeStream(packed, entry.Size),
            0 => throw new ArchiveException("A stored entry of the zip has two sizes: the archive is broken."),
            8 => new DeclaredSizeStream(new DeflateStream(packed, CompressionMode.Decompress), entry.Size),
            _ => throw new ArchiveException($"This entry uses a compression method SFE doesn't read ({entry.Method})."),
        };
    }

    private static ArchiveEntryKind KindOf(string name, int madeBy, uint attributes)
    {
        if (name.EndsWith('/') || name.EndsWith('\\'))
            return ArchiveEntryKind.Folder;
        if (madeBy >> 8 == 3)
        {
            // Made on Unix: the upper half holds the file's mode.
            return ((attributes >> 16) & 0xF000) switch
            {
                0xA000 => ArchiveEntryKind.Link,
                0x4000 => ArchiveEntryKind.Folder,
                0x8000 or 0 => ArchiveEntryKind.File,
                _ => ArchiveEntryKind.Other,
            };
        }
        return (attributes & 0x10) != 0 ? ArchiveEntryKind.Folder : ArchiveEntryKind.File;
    }

    /// <summary>
    /// UTF-8 when flagged; otherwise UTF-8 when the bytes are valid UTF-8 (macOS and many tools write it unflagged),
    /// else code page 437, as the format says.
    /// </summary>
    private static string DecodeName(ReadOnlySpan<byte> bytes, bool utf8)
    {
        if (utf8 || !bytes.ContainsAnyInRange((byte)0x80, (byte)0xFF))
            return Encoding.UTF8.GetString(bytes);
        try
        {
            return StrictUtf8.GetString(bytes);
        }
        catch (DecoderFallbackException)
        {
            return Dos.GetString(bytes);
        }
    }

    private static DateTime? DosTime(int date, int time)
    {
        int day = date & 31, month = (date >> 5) & 15, year = 1980 + (date >> 9);
        int second = (time & 31) * 2, minute = (time >> 5) & 63, hour = time >> 11;
        if (day == 0 || month is 0 or > 12 || day > DateTime.DaysInMonth(year, month) || hour > 23 || minute > 59 || second > 59)
            return null;
        return new DateTime(year, month, day, hour, minute, second, DateTimeKind.Unspecified);
    }

    private static Encoding CodePage437()
    {
        Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);
        return Encoding.GetEncoding(437);
    }

    private static uint ReadU32(Stream stream, long position)
    {
        Span<byte> bytes = stackalloc byte[4];
        stream.Position = position;
        return stream.ReadAtLeast(bytes, 4, throwOnEndOfStream: false) == 4 ? BinaryPrimitives.ReadUInt32LittleEndian(bytes) : 0;
    }

    private static ushort U16(byte[] bytes, int at) => BinaryPrimitives.ReadUInt16LittleEndian(bytes.AsSpan(at));
    private static uint U32(byte[] bytes, int at) => BinaryPrimitives.ReadUInt32LittleEndian(bytes.AsSpan(at));
    private static ulong U64(byte[] bytes, int at) => BinaryPrimitives.ReadUInt64LittleEndian(bytes.AsSpan(at));
    private static long Long(byte[] bytes, int at) => (long)Math.Min(U64(bytes, at), long.MaxValue);
}
