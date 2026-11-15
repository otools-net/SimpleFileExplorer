namespace Otools.Net.Sfe.Core.Transfers;

/// <summary>
/// One side of a transfer between the library and a connection (agents/docs/features/transfers.md): what a
/// transfer job reads and writes through, whatever the side. Paths are relative to the side's root and already
/// normalised. Failures are <see cref="Otools.Net.Sfe.Core.Connections.RemoteStorageException"/> on both sides (the
/// library's side turns its own into them), so the job handles one kind of error. Access is the side's to check.
/// </summary>
public interface ITransferEndpoint : IAsyncDisposable
{
    /// <summary>The name of the content hash <see cref="TransferEntry.Hash"/> carries; null when none.</summary>
    string? HashName { get; }

    /// <summary>The item at <paramref name="path"/>; null when nothing is there.</summary>
    Task<TransferEntry?> GetAsync(string path, CancellationToken cancellationToken);

    /// <summary>The items of a folder, page by page as the side gives them (bounded memory).</summary>
    IAsyncEnumerable<TransferEntry> ListAsync(string folder, CancellationToken cancellationToken);

    /// <summary>A file's content from <paramref name="offset"/> on (0: the whole file).</summary>
    Task<Stream> OpenReadAsync(TransferEntry file, long offset, CancellationToken cancellationToken);

    /// <summary>Starts writing the file <paramref name="name"/> into <paramref name="folder"/>.</summary>
    Task<ITransferWrite> BeginWriteAsync(
        string folder,
        string name,
        long size,
        DateTime? modified,
        CancellationToken cancellationToken
    );

    /// <summary>Makes the folder (its parent exists); answers it, or the folder already there.</summary>
    Task<TransferEntry> CreateFolderAsync(string folder, string name, CancellationToken cancellationToken);

    /// <summary>A name not taken in <paramref name="folder"/>, from <paramref name="name"/> (" (2)" added).</summary>
    Task<string> FreeNameAsync(string folder, string name, bool isFolder, CancellationToken cancellationToken);

    /// <summary>Takes away what is at a name before it is replaced: to the side's trash when it has one.</summary>
    Task RemoveForReplaceAsync(TransferEntry existing, CancellationToken cancellationToken);

    /// <summary>Removes a moved file or emptied folder from the source once verified: to the side's trash.</summary>
    Task DeleteSourceAsync(TransferEntry entry, CancellationToken cancellationToken);

    /// <summary>The free space where <paramref name="folder"/> is.</summary>
    Task<TransferSpace> FreeSpaceAsync(string folder, CancellationToken cancellationToken);

    /// <summary>Told once a file arrived (the library indexes it, applying the folder's default tags).</summary>
    Task AfterWriteAsync(TransferEntry written, CancellationToken cancellationToken);

    /// <summary>The library's tags on a file of this side (its index row); none when it has no row.</summary>
    Task<IReadOnlyList<int>> TagsAsync(TransferEntry file, CancellationToken cancellationToken) =>
        Task.FromResult<IReadOnlyList<int>>([]);

    /// <summary>
    /// Gives the library's tags to a file that arrived (told after <see cref="AfterWriteAsync"/>); false when the side
    /// has no index row of it to carry them.
    /// </summary>
    Task<bool> TagAsync(TransferEntry written, IReadOnlyList<int> tagIds, CancellationToken cancellationToken) =>
        Task.FromResult(false);
}
