namespace Sfe.Core.Files.Compression;

/// <summary>
/// <paramref name="length"/> bytes of a file from <paramref name="start"/>, seekable: a stored zip entry served with
/// Range. Owns the file.
/// </summary>
internal sealed class SliceStream(Stream file, long start, long length) : Stream
{
    private long _position;

    public override bool CanRead => true;
    public override bool CanSeek => true;
    public override bool CanWrite => false;
    public override long Length => length;

    public override long Position
    {
        get => _position;
        set => _position = value is >= 0 ? value : throw new ArgumentOutOfRangeException(nameof(value));
    }

    public override int Read(byte[] buffer, int offset, int count) => Read(buffer.AsSpan(offset, count));

    public override int Read(Span<byte> buffer)
    {
        var left = length - _position;
        if (left <= 0)
            return 0;
        file.Position = start + _position;
        var read = file.Read(buffer[..(int)Math.Min(buffer.Length, left)]);
        _position += read;
        return read;
    }

    public override long Seek(long offset, SeekOrigin origin) =>
        Position = origin switch
        {
            SeekOrigin.Begin => offset,
            SeekOrigin.Current => _position + offset,
            _ => length + offset,
        };

    public override void Flush() { }

    public override void SetLength(long value) => throw new NotSupportedException();

    public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();

    protected override void Dispose(bool disposing)
    {
        if (disposing)
            file.Dispose();
        base.Dispose(disposing);
    }
}
