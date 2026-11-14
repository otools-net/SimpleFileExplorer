using System.Buffers;
using Sfe.Core.Connections;
using Sfe.Core.Files;

namespace Sfe.Core.Transfers;

/// <summary>
/// One file's bytes streamed from the source into an <see cref="ITransferWrite"/>, part by part: the source read
/// again from where the write says it is (<see cref="ITransferWrite.Offset"/> moved back by the provider, a part that
/// failed), each part sent at most <see cref="PartTries"/> times from the same offset, the hashes fed with the bytes
/// the target took, in order, once each, and the job's progress moved by the bytes that arrived.
/// </summary>
internal sealed class TransferStream(JobRun job, ITransferEndpoint source, TransferEntry file, TransferHashes hashes)
    : IAsyncDisposable
{
    /// <summary>The largest part read and written in one go.</summary>
    public const int MaxPart = 32 << 20;

    /// <summary>Tries of a part from the same offset: the first, then two more.</summary>
    public const int PartTries = 3;

    /// <summary>The hashes fed with the file's bytes.</summary>
    public TransferHashes Hashes => hashes;

    /// <summary>The file's name, for messages.</summary>
    public string Name => file.Name;

    private Stream? _reader;
    private long _readerAt = -1;
    private long _hashed;
    private long _counted;
    private long _failedAt = -1;
    private int _failures;

    /// <summary>Sends the whole file (<paramref name="size"/> bytes) through <paramref name="write"/>.</summary>
    public async Task CopyAsync(ITransferWrite write, long size, int partSize, CancellationToken token)
    {
        var buffer = ArrayPool<byte>.Shared.Rent((int)Math.Max(1, Math.Min(partSize, size)));
        try
        {
            while (write.Offset < size)
                await PartAsync(write, buffer.AsMemory(0, (int)Math.Min(partSize, size - write.Offset)), token);
        }
        finally
        {
            ArrayPool<byte>.Shared.Return(buffer);
        }
    }

    public async ValueTask DisposeAsync() => await CloseReaderAsync();

    /// <summary>The next part: read at the write's offset and sent; a failure it survives is tried again.</summary>
    private async Task PartAsync(ITransferWrite write, Memory<byte> buffer, CancellationToken token)
    {
        var offset = write.Offset;
        int length;
        try
        {
            length = await SendAsync(write, offset, buffer, token);
        }
        catch (RemoteStorageException failure) when (Passing(failure.Kind) && MayTryAgain(offset))
        {
            // Read again from where the write is: the source's stream is past this part.
            await CloseReaderAsync();
            return;
        }
        Took(buffer.Span[..length], offset, write.Offset);
    }

    /// <summary>Reads the part at <paramref name="offset"/> and writes it; answers its length.</summary>
    private async Task<int> SendAsync(ITransferWrite write, long offset, Memory<byte> buffer, CancellationToken token)
    {
        var length = await ReadAsync(offset, buffer, token);
        await write.WriteAsync(buffer[..length], token);
        return length;
    }

    /// <summary>The part at <paramref name="offset"/>, the source opened again there when it is elsewhere.</summary>
    private async Task<int> ReadAsync(long offset, Memory<byte> buffer, CancellationToken token)
    {
        if (_reader is null || _readerAt != offset)
        {
            await CloseReaderAsync();
            _reader = await source.OpenReadAsync(file, offset, token);
            _readerAt = offset;
        }
        var length = await FillAsync(_reader, buffer, token);
        if (length == 0)
            throw new RemoteStorageException(RemoteErrorKind.Unavailable, $"“{file.Name}” ended before its size.");
        _readerAt += length;
        return length;
    }

    /// <summary>
    /// The target took the part up to <paramref name="reached"/> (its new offset): the bytes not hashed yet are fed to
    /// the hashes, the job's progress moves.
    /// </summary>
    private void Took(ReadOnlySpan<byte> part, long offset, long reached)
    {
        if (reached > offset + part.Length)
        {
            throw new RemoteStorageException(
                RemoteErrorKind.Unavailable,
                $"The storage holds more of “{file.Name}” than was sent."
            );
        }
        if (reached > _hashed && _hashed >= offset)
        {
            hashes.Append(part[(int)(_hashed - offset)..(int)(reached - offset)]);
            _hashed = reached;
        }
        if (reached > _counted)
        {
            job.AddBytes(reached - _counted);
            _counted = reached;
        }
    }

    /// <summary>
    /// A part at <paramref name="offset"/> may be tried again: fewer than <see cref="PartTries"/> failures.
    /// </summary>
    private bool MayTryAgain(long offset)
    {
        if (offset != _failedAt)
            (_failedAt, _failures) = (offset, 0);
        return ++_failures < PartTries;
    }

    private async Task CloseReaderAsync()
    {
        if (_reader is not null)
            await _reader.DisposeAsync();
        _reader = null;
    }

    /// <summary>Reads until the buffer is full or the stream ends; the stream's failures as transfer errors.</summary>
    private async Task<int> FillAsync(Stream stream, Memory<byte> buffer, CancellationToken token)
    {
        var filled = 0;
        try
        {
            int read;
            while (filled < buffer.Length && (read = await stream.ReadAsync(buffer[filled..], token)) > 0)
                filled += read;
        }
        catch (IOException ex)
        {
            throw new RemoteStorageException(
                RemoteErrorKind.Unavailable,
                $"“{file.Name}” could not be read.",
                inner: ex
            );
        }
        return filled;
    }

    /// <summary>A failure that trying again may get past (the provider down or busy, the disk busy).</summary>
    private static bool Passing(RemoteErrorKind kind) =>
        kind is RemoteErrorKind.Unavailable or RemoteErrorKind.RateLimited;
}
