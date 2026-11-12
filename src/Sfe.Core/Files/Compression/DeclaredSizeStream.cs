namespace Sfe.Core.Files.Compression;

/// <summary>
/// An entry's data held to the size its archive declared: one byte more, or one less at the end, is a broken (or
/// hostile) archive, never a longer file. This is what stops a zip bomb whose headers lie. A negative size (a format
/// that declares none, such as one <c>.gz</c>) only counts; its callers bound it otherwise.
/// </summary>
internal sealed class DeclaredSizeStream(Stream inner, long size, IDisposable? owner = null) : Stream
{
    private long _position;

    public override bool CanRead => true;
    public override bool CanSeek => false;
    public override bool CanWrite => false;
    public override long Length => size >= 0 ? size : throw new NotSupportedException();
    public override long Position
    {
        get => _position;
        set => throw new NotSupportedException();
    }

    public override int Read(byte[] buffer, int offset, int count) => Read(buffer.AsSpan(offset, count));

    public override int Read(Span<byte> buffer)
    {
        if (buffer.Length == 0)
            return 0;
        int read;
        try
        {
            // One byte past the size is asked for, so that more data than declared is seen.
            read = inner.Read(size < 0 ? buffer : buffer[..(int)Math.Min(buffer.Length, size - _position + 1)]);
        }
        catch (InvalidDataException error)
        {
            throw new ArchiveException("An entry's data is damaged: the archive is broken.", error);
        }
        _position += read;
        if (size < 0)
            return read;
        if (_position > size)
            throw new ArchiveException("An entry holds more data than its archive declares: the archive is broken.");
        if (read == 0 && _position < size)
            throw new ArchiveException("An entry ends before the size its archive declares: the archive is broken.");
        return read;
    }

    public override void Flush() { }

    public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();

    public override void SetLength(long value) => throw new NotSupportedException();

    public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            inner.Dispose();
            owner?.Dispose();
        }
        base.Dispose(disposing);
    }
}
