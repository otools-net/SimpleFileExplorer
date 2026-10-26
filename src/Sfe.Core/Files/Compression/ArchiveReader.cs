using SharpCompress.Archives.SevenZip;
using SharpCompress.Readers;

namespace Sfe.Core.Files.Compression;

/// <summary>
/// Reads an archive's list of entries and one entry's data, never unpacking anything else
/// (agents/docs/features/archive-reading.md). Zip goes through <see cref="ZipDirectory"/>; the others
/// through SharpCompress: its forward reader for rar and tars (headers spread over the file, constant memory), its
/// archive for 7z (whose header sits at the end and is read whole).
/// </summary>
public static class ArchiveReader
{
    private const int BufferSize = 1024 * 1024;

    /// <summary>
    /// The entries of the archive at <paramref name="fullPath"/>, in the archive's order, as they are read (a caller
    /// stops when it has enough). Errors of the archive come as <see cref="ArchiveException"/>. <paramref name="password"/>
    /// opens a 7z or rar whose list is encrypted (a zip's names are never encrypted). A 7z whose list is larger than
    /// <paramref name="maxSevenZipList"/> bytes is refused before it is read (<see cref="SevenZipList"/>).
    /// </summary>
    public static IEnumerable<ArchiveEntry> ReadEntries(string fullPath, ArchiveFormat format, CancellationToken cancel = default, string? password = null,
        long maxSevenZipList = SevenZipList.DefaultMaxMegabytes * 1024L * 1024) =>
        format switch
        {
            ArchiveFormat.Zip => ReadZip(fullPath, cancel),
            ArchiveFormat.SevenZip => ReadSevenZip(fullPath, cancel, password, maxSevenZipList),
            ArchiveFormat.TarGzip or ArchiveFormat.TarBzip2 or ArchiveFormat.TarXz => ReadCompressedTar(fullPath, format, cancel),
            _ => ReadForward(fullPath, format, cancel, password),
        };

    /// <summary>
    /// The data of <paramref name="entry"/> (as listed by <see cref="ReadEntries"/>), decrypted with
    /// <paramref name="password"/> when it is encrypted, held to its declared size. A solid 7z or rar, or a compressed
    /// tar, unpacks the entries before it to reach it: past <paramref name="maxSkipped"/> bytes of those (as declared,
    /// which is what the decoder produces), it is refused (<see cref="ArchiveException.TooFar"/>), before any is unpacked.
    /// </summary>
    public static Stream OpenEntry(string fullPath, ArchiveFormat format, ArchiveEntry entry, string? password = null, long maxSkipped = long.MaxValue,
        long maxSevenZipList = SevenZipList.DefaultMaxMegabytes * 1024L * 1024)
    {
        if (entry.Kind != ArchiveEntryKind.File)
            throw new ArchiveException("Only files of an archive have data.");
        if (entry.Encrypted && string.IsNullOrEmpty(password))
            throw new ArchiveException("This entry is protected by a password.", needsPassword: true);
        var stream = Open(fullPath);
        try
        {
            if (format == ArchiveFormat.Zip)
                return ZipDirectory.Locate(stream).OpenEntry(stream, entry, password);
            return Wrap(() => format == ArchiveFormat.SevenZip
                ? OpenSevenZip(stream, entry, password, maxSkipped, maxSevenZipList)
                : OpenForward(stream, format, entry, password, maxSkipped));
        }
        catch
        {
            stream.Dispose();
            throw;
        }
    }

    /// <summary>
    /// A stored, unencrypted file of a zip as a seekable slice of the archive (its data as it is, with no decompression:
    /// a video seeks in it); null for anything else (other formats, deflated or encrypted entries).
    /// </summary>
    public static Stream? OpenStored(string fullPath, ArchiveFormat format, ArchiveEntry entry)
    {
        if (format != ArchiveFormat.Zip || entry.Encrypted || entry.Method != 0)
            return null;
        var stream = Open(fullPath);
        try
        {
            if (ZipDirectory.Locate(stream).StoredRange(stream, entry) is not { } range)
            {
                stream.Dispose();
                return null;
            }
            return new SliceStream(stream, range.Start, range.Length);
        }
        catch
        {
            stream.Dispose();
            throw;
        }
    }

    /// <summary>
    /// Every entry of the archive in the archive's order, each with its data (files only), for extracting: one pass
    /// over the file, so a solid 7z or rar block is unpacked once whatever is taken from it. <paramref name="visit"/>
    /// reads the stream it is given (or not) before returning: the stream ends with the visit. <paramref name="read"/>
    /// is told how far into the archive's file the pass is, after each entry.
    /// </summary>
    public static void ReadThrough(string fullPath, ArchiveFormat format, string? password, Action<ArchiveEntry, Func<Stream>> visit,
        Action<long>? read = null, CancellationToken cancel = default, long maxSevenZipList = SevenZipList.DefaultMaxMegabytes * 1024L * 1024)
    {
        switch (format)
        {
            case ArchiveFormat.Zip:
                ReadThroughZip(fullPath, password, visit, read, cancel);
                break;
            case ArchiveFormat.SevenZip:
                {
                    using var stream = Open(fullPath);
                    SevenZipList.Check(stream, maxSevenZipList);
                    using var archive = Wrap(() => SevenZipArchive.OpenArchive(stream, Options(password)));
                    using var reader = Wrap(archive.ExtractAllEntries);
                    ReadThroughReader(reader, password, visit, () => read?.Invoke(stream.Position), cancel);
                    break;
                }
            case ArchiveFormat.TarGzip or ArchiveFormat.TarBzip2 or ArchiveFormat.TarXz:
                {
                    using var file = Open(fullPath);
                    using var tail = new TailStream(Decompress(file, format));
                    using var reader = Wrap(() => ReaderFactory.OpenReader(tail, Options()));
                    ReadThroughReader(reader, password, visit, () => read?.Invoke(file.Position), cancel);
                    Wrap(() =>
                    {
                        tail.CopyTo(Stream.Null);
                        return 0;
                    });
                    if (!tail.EndsWithTarMark)
                        throw CutOff();
                    break;
                }
            default:
                {
                    using var stream = Open(fullPath);
                    using var reader = Wrap(() => ReaderFactory.OpenReader(stream, Options(password)));
                    ReadThroughReader(reader, password, visit, () => read?.Invoke(stream.Position), cancel);
                    if (format == ArchiveFormat.Tar && !EndsWithTarMark(stream))
                        throw CutOff();
                    break;
                }
        }
    }

    /// <summary>A zip: its directory read from one handle, the entries' data from another (no seeking back and forth).</summary>
    private static void ReadThroughZip(string fullPath, string? password, Action<ArchiveEntry, Func<Stream>> visit, Action<long>? read, CancellationToken cancel)
    {
        using var list = Open(fullPath);
        using var data = Open(fullPath);
        var directory = ZipDirectory.Locate(list);
        foreach (var entry in directory.Read(list, cancel))
        {
            if (entry.Kind != ArchiveEntryKind.File)
            {
                visit(entry, NoData);
                continue;
            }
            Stream? opened = null;
            try
            {
                visit(entry, () => opened ??= Locked(entry, password) ?? directory.OpenEntry(new KeptOpen(data), entry, password));
            }
            finally
            {
                opened?.Dispose();
            }
            read?.Invoke(data.Position);
        }
    }

    private static void ReadThroughReader(IReader reader, string? password, Action<ArchiveEntry, Func<Stream>> visit, Action read, CancellationToken cancel)
    {
        for (var index = 0L; Wrap(reader.MoveToNextEntry); index++)
        {
            cancel.ThrowIfCancellationRequested();
            var entry = Map(reader.Entry, index);
            if (entry.Kind != ArchiveEntryKind.File)
            {
                visit(entry, NoData);
                continue;
            }
            Stream? opened = null;
            try
            {
                // Errors of the data (a wrong password, a damaged block) come out of SharpCompress as it is read.
                visit(entry, () => opened ??= Locked(entry, password) ?? Bounded(new WrappedErrors(Wrap(reader.OpenEntryStream)), entry, null));
            }
            finally
            {
                opened?.Dispose();
            }
            read();
        }
    }

    private static Stream NoData() => throw new ArchiveException("Only files of an archive have data.");

    /// <summary>An encrypted entry opened without a password: refused (only when it is taken, not when it is passed).</summary>
    private static Stream? Locked(ArchiveEntry entry, string? password) =>
        entry.Encrypted && string.IsNullOrEmpty(password) ? throw new ArchiveException("This entry is protected by a password.", needsPassword: true) : null;

    private static IEnumerable<ArchiveEntry> ReadZip(string fullPath, CancellationToken cancel)
    {
        using var stream = Open(fullPath);
        var directory = ZipDirectory.Locate(stream);
        foreach (var entry in directory.Read(stream, cancel))
            yield return entry;
    }

    private static IEnumerable<ArchiveEntry> ReadSevenZip(string fullPath, CancellationToken cancel, string? password, long maxList)
    {
        using var stream = Open(fullPath);
        SevenZipList.Check(stream, maxList);
        using var archive = Wrap(() => SevenZipArchive.OpenArchive(stream, Options(password)));
        using var entries = Wrap(() => archive.Entries.GetEnumerator());
        for (var index = 0L; Wrap(entries.MoveNext); index++)
        {
            cancel.ThrowIfCancellationRequested();
            yield return Map(entries.Current, index);
        }
    }

    private static IEnumerable<ArchiveEntry> ReadForward(string fullPath, ArchiveFormat format, CancellationToken cancel, string? password)
    {
        using var stream = Open(fullPath);
        using var reader = Wrap(() => ReaderFactory.OpenReader(stream, Options(password)));
        for (var index = 0L; Wrap(reader.MoveToNextEntry); index++)
        {
            cancel.ThrowIfCancellationRequested();
            yield return Map(reader.Entry, index);
        }
        if (format == ArchiveFormat.Tar && !EndsWithTarMark(stream))
            throw CutOff();
    }

    /// <summary>
    /// A compressed tar, decompressed here rather than by SharpCompress: a gzip cut short just ends there (no error,
    /// checked 2026-10-26), so the tar's own end mark, seen through <see cref="TailStream"/>, tells a whole list from
    /// the first part of one.
    /// </summary>
    private static IEnumerable<ArchiveEntry> ReadCompressedTar(string fullPath, ArchiveFormat format, CancellationToken cancel)
    {
        using var file = Open(fullPath);
        using var tail = new TailStream(Decompress(file, format));
        using var reader = Wrap(() => ReaderFactory.OpenReader(tail, Options()));
        for (var index = 0L; Wrap(reader.MoveToNextEntry); index++)
        {
            cancel.ThrowIfCancellationRequested();
            yield return Map(reader.Entry, index);
        }
        // What follows the end mark is padding: reading it to the end is cheap.
        Wrap(() =>
        {
            tail.CopyTo(Stream.Null);
            return 0;
        });
        if (!tail.EndsWithTarMark)
            throw CutOff();
    }

    private static Stream Decompress(FileStream file, ArchiveFormat format) => format switch
    {
        ArchiveFormat.TarGzip => new System.IO.Compression.GZipStream(file, System.IO.Compression.CompressionMode.Decompress),
        ArchiveFormat.TarBzip2 => SharpCompress.Compressors.BZip2.BZip2Stream.Create(file,
            SharpCompress.Compressors.CompressionMode.Decompress, decompressConcatenated: true, leaveOpen: false, tolerateTruncatedStream: false),
        _ => new SharpCompress.Compressors.Xz.XZStream(file),
    };

    /// <summary>A whole tar ends with two zero blocks (then zeros to its record size): its length is whole blocks.</summary>
    private static bool EndsWithTarMark(FileStream stream)
    {
        var length = stream.Length;
        if (length < 1024 || length % 512 != 0)
            return false;
        var end = new byte[1024];
        stream.Position = length - end.Length;
        stream.ReadExactly(end);
        return !end.AsSpan().ContainsAnyExcept((byte)0);
    }

    private static void TooFar(long skipped, long maxSkipped)
    {
        if (skipped > maxSkipped)
            throw new ArchiveException(
                $"To reach this file the archive would unpack {skipped / (1024 * 1024):N0} MB before it (it is packed together with them): extract it, or download the archive.")
            { TooFar = true };
    }

    private static ArchiveException CutOff() =>
        new("The archive ends without its end mark: it is cut off or broken. Only the entries before the break are listed.");

    private static Stream OpenSevenZip(FileStream stream, ArchiveEntry entry, string? password, long maxSkipped, long maxList)
    {
        SevenZipList.Check(stream, maxList);
        var archive = SevenZipArchive.OpenArchive(stream, Options(password));
        try
        {
            // What the decoder unpacks first: the files before it in its block (IsSolid: shares the previous one's block).
            var skipped = 0L;
            var index = 0L;
            SharpCompress.Archives.IArchiveEntry? found = null;
            foreach (var candidate in archive.Entries)
            {
                if (index++ == entry.Locator)
                {
                    found = candidate;
                    break;
                }
                if (candidate.IsDirectory || candidate.Size <= 0)
                    continue;
                skipped = (candidate.IsSolid ? skipped : 0) + candidate.Size;
            }
            if (found is null)
                throw new ArchiveException("The entry is no longer in the archive.");
            TooFar(found.IsSolid ? skipped : 0, maxSkipped);
            return Bounded(found.OpenEntryStream(), entry, new Owner(archive, stream));
        }
        catch
        {
            archive.Dispose();
            throw;
        }
    }

    /// <summary>
    /// Rar and tars have no index: the reader walks to the entry, skipping the others' data (unpacked in a compressed tar
    /// and a solid rar, passed over in a plain tar and another rar).
    /// </summary>
    private static Stream OpenForward(FileStream stream, ArchiveFormat format, ArchiveEntry entry, string? password, long maxSkipped)
    {
        var reader = ReaderFactory.OpenReader(stream, Options(password));
        try
        {
            // The reader marks every file of a solid rar solid, the first too.
            bool Unpacked(SharpCompress.Common.IEntry current) => format == ArchiveFormat.Rar ? current.IsSolid : format != ArchiveFormat.Tar;
            var skipped = 0L;
            for (var index = 0L; ; index++)
            {
                if (!reader.MoveToNextEntry())
                    throw new ArchiveException("The entry is no longer in the archive.");
                if (index == entry.Locator)
                    break;
                // Checked before the next move unpacks this one's data.
                if (Unpacked(reader.Entry) && !reader.Entry.IsDirectory)
                    TooFar(skipped += Math.Max(reader.Entry.Size, 0), maxSkipped);
            }
            if (!Unpacked(reader.Entry))
                skipped = 0;
            TooFar(skipped, maxSkipped);
            return Bounded(reader.OpenEntryStream(), entry, new Owner(reader, stream));
        }
        catch
        {
            reader.Dispose();
            throw;
        }
    }

    private static Stream Bounded(Stream data, ArchiveEntry entry, IDisposable owner) =>
        new DeclaredSizeStream(data, entry.Size, owner);

    private static ArchiveEntry Map(SharpCompress.Common.IEntry entry, long index)
    {
        var kind = entry.IsDirectory ? ArchiveEntryKind.Folder
            : !string.IsNullOrEmpty(entry.LinkTarget) ? ArchiveEntryKind.Link
            : ArchiveEntryKind.File;
        return new ArchiveEntry(entry.Key ?? "", kind, entry.Size, entry.CompressedSize, entry.LastModifiedTime, entry.IsEncrypted, index);
    }

    private static ReaderOptions Options(string? password = null) =>
        new() { LeaveStreamOpen = true, Password = string.IsNullOrEmpty(password) ? null : password };

    private static FileStream Open(string fullPath) =>
        new(fullPath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete, BufferSize, FileOptions.SequentialScan);

    /// <summary>SharpCompress' own errors (format, password, cut-off file) as one <see cref="ArchiveException"/>.</summary>
    private static T Wrap<T>(Func<T> read)
    {
        try
        {
            return read();
        }
        catch (Exception error) when (error is not (ArchiveException or OperationCanceledException or OutOfMemoryException))
        {
            var password = error.GetType().Name.Contains("Password", StringComparison.OrdinalIgnoreCase)
                || error.GetType().Name.Contains("Crypt", StringComparison.OrdinalIgnoreCase)
                || error.Message.Contains("password", StringComparison.OrdinalIgnoreCase);
            throw new ArchiveException(
                password ? "The archive is protected by a password." : "The archive can't be read: it is broken, cut off or of a kind SFE doesn't read.",
                error, password);
        }
    }

    /// <summary>A decompressed stream read through, keeping its length and last 1 KB (where a tar's end mark is).</summary>
    private sealed class TailStream(Stream inner) : Stream
    {
        private readonly byte[] last = new byte[1024];
        private long length;

        public bool EndsWithTarMark =>
            length >= last.Length && length % 512 == 0 && !last.AsSpan().ContainsAnyExcept((byte)0);

        public override bool CanRead => true;
        public override bool CanSeek => false;
        public override bool CanWrite => false;
        public override long Length => throw new NotSupportedException();
        public override long Position { get => length; set => throw new NotSupportedException(); }

        public override int Read(byte[] buffer, int offset, int count) => Read(buffer.AsSpan(offset, count));

        public override int Read(Span<byte> buffer)
        {
            var read = inner.Read(buffer);
            var data = buffer[..read];
            if (data.Length >= last.Length)
            {
                data[^last.Length..].CopyTo(last);
            }
            else if (data.Length > 0)
            {
                last.AsSpan(data.Length).CopyTo(last);
                data.CopyTo(last.AsSpan(last.Length - data.Length));
            }
            length += read;
            return read;
        }

        public override void Flush() { }
        public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();
        public override void SetLength(long value) => throw new NotSupportedException();
        public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();

        protected override void Dispose(bool disposing)
        {
            if (disposing)
                inner.Dispose();
            base.Dispose(disposing);
        }
    }

    /// <summary>The archive's file under one entry's stream: the entry ends, the file stays open for the next.</summary>
    private sealed class KeptOpen(Stream inner) : ReadOnlyStream(inner)
    {
        public override bool CanSeek => true;
        public override long Length => Inner.Length;
        public override long Position { get => Inner.Position; set => Inner.Position = value; }
        public override long Seek(long offset, SeekOrigin origin) => Inner.Seek(offset, origin);
        public override int Read(Span<byte> buffer) => Inner.Read(buffer);

        protected override void Dispose(bool disposing)
        {
        }
    }

    /// <summary>SharpCompress' errors while an entry's data is read (a wrong password, a damaged block), as <see cref="ArchiveException"/>.</summary>
    private sealed class WrappedErrors(Stream inner) : ReadOnlyStream(inner)
    {
        public override int Read(Span<byte> buffer)
        {
            try
            {
                return Inner.Read(buffer);
            }
            catch (Exception error) when (error is not (ArchiveException or OperationCanceledException or OutOfMemoryException))
            {
                return Wrap<int>(() => throw error);
            }
        }

        // SharpCompress reads the rest of an entry when its stream is closed: its errors come then too.
        protected override void Dispose(bool disposing) =>
            Wrap(() =>
            {
                base.Dispose(disposing);
                return 0;
            });
    }

    /// <summary>What an entry's stream keeps open: the reader (or archive), then the file.</summary>
    private sealed class Owner(IDisposable reader, Stream file) : IDisposable
    {
        public void Dispose()
        {
            reader.Dispose();
            file.Dispose();
        }
    }
}
