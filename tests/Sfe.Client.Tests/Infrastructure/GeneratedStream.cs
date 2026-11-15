namespace Otools.Net.Sfe.Client.Tests.Infrastructure;

/// <summary>
/// A stream that can't seek, of <paramref name="length"/> bytes made as they are read (byte i is
/// <see cref="ByteAt"/>), so that a large upload needs no memory on the test's side; it keeps how far it was read.
/// </summary>
public sealed class GeneratedStream(long length) : Stream
{
    private long _position;

    /// <summary>The bytes read from it so far.</summary>
    public long BytesRead => Interlocked.Read(ref _position);

    public override bool CanRead => true;
    public override bool CanSeek => false;
    public override bool CanWrite => false;
    public override long Length => throw new NotSupportedException();

    public override long Position
    {
        get => throw new NotSupportedException();
        set => throw new NotSupportedException();
    }

    /// <summary>The byte at <paramref name="index"/> of every generated stream.</summary>
    public static byte ByteAt(long index) => (byte)((index * 31 + index / 4096) % 251);

    public override int Read(byte[] buffer, int offset, int count)
    {
        var read = (int)Math.Min(count, length - _position);
        for (var i = 0; i < read; i++)
            buffer[offset + i] = ByteAt(_position + i);
        Interlocked.Add(ref _position, read);
        return read;
    }

    public override void Flush() { }

    public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();

    public override void SetLength(long value) => throw new NotSupportedException();

    public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();
}
