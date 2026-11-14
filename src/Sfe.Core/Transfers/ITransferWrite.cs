namespace Sfe.Core.Transfers;

/// <summary>
/// A file being written on one side of a transfer, part after part: into a partial file of the library (renamed at
/// commit), or into the provider's upload session. <see cref="Offset"/> says where the next part starts; a side that
/// was told another offset (the provider's) moves it, and the source is read again from there.
/// </summary>
public interface ITransferWrite : IAsyncDisposable
{
    long Offset { get; }

    /// <summary>Writes the next part, which starts at <see cref="Offset"/>.</summary>
    Task WriteAsync(ReadOnlyMemory<byte> part, CancellationToken cancellationToken);

    /// <summary>
    /// Makes the file under its name once every part is written; <c>RemoteErrorKind.Exists</c> when the name was
    /// taken meanwhile. Answers the file as written (its size and hash to verify).
    /// </summary>
    Task<TransferEntry> CommitAsync(CancellationToken cancellationToken);

    /// <summary>Drops what was written (a partial file, an upload session).</summary>
    Task AbortAsync(CancellationToken cancellationToken);
}
