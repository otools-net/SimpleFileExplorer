using Otools.Net.Sfe.Core.Connections;

namespace Otools.Net.Sfe.Core.Transfers;

/// <summary>
/// A file written into a connection through the provider's upload session: each part appended at
/// <see cref="Offset"/> (split at the provider's largest part), the file made at commit, never over another item.
/// When the provider says it holds another offset (a part arrived whose answer was lost), <see cref="Offset"/> moves
/// to it and the job reads its source again from there. Nothing to drop at the provider on abort: sessions expire.
/// </summary>
public sealed class RemoteTransferWrite(RemoteTransferEndpoint endpoint, string upload, RemoteUploadTarget target)
    : ITransferWrite
{
    private bool _ended;

    public long Offset { get; private set; }

    public async Task WriteAsync(ReadOnlyMemory<byte> part, CancellationToken cancellationToken)
    {
        ObjectDisposedException.ThrowIf(_ended, this);
        var most = (int)Math.Min(endpoint.MaxUploadPart ?? int.MaxValue, int.MaxValue);
        while (!part.IsEmpty)
        {
            var piece = part[..Math.Min(most, part.Length)];
            try
            {
                await endpoint.AppendAsync(upload, Offset, piece, cancellationToken);
            }
            catch (RemoteStorageException failure)
                when (failure is { Kind: RemoteErrorKind.WrongOffset, Offset: { } held })
            {
                Offset = held;
                return;
            }
            Offset += piece.Length;
            part = part[piece.Length..];
        }
    }

    public async Task<TransferEntry> CommitAsync(CancellationToken cancellationToken)
    {
        ObjectDisposedException.ThrowIf(_ended, this);
        var written = await endpoint.FinishAsync(upload, target with { Length = Offset }, cancellationToken);
        _ended = true;
        return written;
    }

    public Task AbortAsync(CancellationToken cancellationToken)
    {
        _ended = true;
        return Task.CompletedTask;
    }

    public ValueTask DisposeAsync()
    {
        _ended = true;
        return ValueTask.CompletedTask;
    }
}
