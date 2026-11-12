namespace Sfe.Core.Files.Compression;

/// <summary>
/// A window of <paramref name="length"/> bytes of <paramref name="inner"/> from <paramref name="start"/>: an entry's
/// packed data, so a decompressor never reads its neighbours or the directory.
/// </summary>
internal sealed class WindowStream(Stream inner, long start, long length, IDisposable? owner = null) : Stream
{
    private long _position;
    private bool _positioned;

    public override bool CanRead => true;
    public override bool CanSeek => false;
    public override bool CanWrite => false;
    public override long Length => length;
    public override long Position
    {
        get => _position;
        set => throw new NotSupportedException();
    }

    public override int Read(byte[] buffer, int offset, int count) => Read(buffer.AsSpan(offset, count));

    public override int Read(Span<byte> buffer)
    {
        if (!_positioned)
        {
            inner.Position = start;
            _positioned = true;
        }
        var left = length - _position;
        if (left <= 0)
            return 0;
        var read = inner.Read(buffer[..(int)Math.Min(buffer.Length, left)]);
        _position += read;
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
