namespace Otools.Net.Sfe.Client.Tests.Infrastructure;

/// <summary>
/// The bytes given, as a stream that can seek, failing once (an <see cref="IOException"/>) when a read reaches
/// <paramref name="failAt"/>: a source that breaks halfway through an upload.
/// </summary>
public sealed class FailingStream(byte[] bytes, long failAt) : MemoryStream(bytes, writable: false)
{
    private bool _failed;

    public override int Read(byte[] buffer, int offset, int count)
    {
        if (!_failed && Position + count > failAt)
        {
            _failed = true;
            throw new IOException("The disk went away.");
        }
        return base.Read(buffer, offset, count);
    }

    public override Task<int> ReadAsync(byte[] buffer, int offset, int count, CancellationToken cancellationToken) =>
        Task.FromResult(Read(buffer, offset, count));

    public override ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken cancellationToken = default)
    {
        var array = new byte[buffer.Length];
        var read = Read(array, 0, array.Length);
        array.AsSpan(0, read).CopyTo(buffer.Span);
        return ValueTask.FromResult(read);
    }
}
