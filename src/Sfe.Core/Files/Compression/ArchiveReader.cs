using SharpCompress.Archives.SevenZip;
using SharpCompress.Readers;

namespace Sfe.Core.Files.Compression;

/// <summary>
/// Reads an archive's list of entries and one entry's data, never unpacking anything else
/// (agents/docs/development/compressed-archives-plan.md). Zip goes through <see cref="ZipDirectory"/>; the others
/// through SharpCompress: its forward reader for rar and tars (headers spread over the file, constant memory), its
/// archive for 7z (whose header sits at the end and is read whole).
/// </summary>
public static class ArchiveReader
{
    private const int BufferSize = 1024 * 1024;

    /// <summary>
    /// The entries of the archive at <paramref name="fullPath"/>, in the archive's order, as they are read (a caller
    /// stops when it has enough). Errors of the archive come as <see cref="ArchiveException"/>. <paramref name="password"/>
    /// opens a 7z or rar whose list is encrypted (a zip's names are never encrypted).
    /// </summary>
    public static IEnumerable<ArchiveEntry> ReadEntries(string fullPath, ArchiveFormat format, CancellationToken cancel = default, string? password = null) =>
        format switch
        {
            ArchiveFormat.Zip => ReadZip(fullPath, cancel),
            ArchiveFormat.SevenZip => ReadSevenZip(fullPath, cancel, password),
            ArchiveFormat.TarGzip or ArchiveFormat.TarBzip2 or ArchiveFormat.TarXz => ReadCompressedTar(fullPath, format, cancel),
            _ => ReadForward(fullPath, format, cancel, password),
        };

    /// <summary>
    /// The data of <paramref name="entry"/> (as listed by <see cref="ReadEntries"/>), decrypted with
    /// <paramref name="password"/> when it is encrypted, held to its declared size.
    /// </summary>
    public static Stream OpenEntry(string fullPath, ArchiveFormat format, ArchiveEntry entry, string? password = null)
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
            return Wrap(() => format == ArchiveFormat.SevenZip ? OpenSevenZip(stream, entry, password) : OpenForward(stream, entry, password));
        }
        catch
        {
            stream.Dispose();
            throw;
        }
    }

    private static IEnumerable<ArchiveEntry> ReadZip(string fullPath, CancellationToken cancel)
    {
        using var stream = Open(fullPath);
        var directory = ZipDirectory.Locate(stream);
        foreach (var entry in directory.Read(stream, cancel))
            yield return entry;
    }

    private static IEnumerable<ArchiveEntry> ReadSevenZip(string fullPath, CancellationToken cancel, string? password)
    {
        using var stream = Open(fullPath);
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
        using var tail = new TailStream(format switch
        {
            ArchiveFormat.TarGzip => new System.IO.Compression.GZipStream(file, System.IO.Compression.CompressionMode.Decompress),
            ArchiveFormat.TarBzip2 => SharpCompress.Compressors.BZip2.BZip2Stream.Create(file,
                SharpCompress.Compressors.CompressionMode.Decompress, decompressConcatenated: true, leaveOpen: false, tolerateTruncatedStream: false),
            _ => new SharpCompress.Compressors.Xz.XZStream(file),
        });
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

    private static ArchiveException CutOff() =>
        new("The archive ends without its end mark: it is cut off or broken. Only the entries before the break are listed.");

    private static Stream OpenSevenZip(FileStream stream, ArchiveEntry entry, string? password)
    {
        var archive = SevenZipArchive.OpenArchive(stream, Options(password));
        try
        {
            var found = archive.Entries.Skip((int)Math.Min(entry.Locator, int.MaxValue)).FirstOrDefault()
                ?? throw new ArchiveException("The entry is no longer in the archive.");
            return Bounded(found.OpenEntryStream(), entry, new Owner(archive, stream));
        }
        catch
        {
            archive.Dispose();
            throw;
        }
    }

    /// <summary>Rar and tars have no index: the reader walks to the entry, skipping the others' data.</summary>
    private static Stream OpenForward(FileStream stream, ArchiveEntry entry, string? password)
    {
        var reader = ReaderFactory.OpenReader(stream, Options(password));
        try
        {
            for (var index = 0L; index <= entry.Locator; index++)
                if (!reader.MoveToNextEntry())
                    throw new ArchiveException("The entry is no longer in the archive.");
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
