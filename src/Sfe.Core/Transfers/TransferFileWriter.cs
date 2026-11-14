using Otools.Net.Sfe.Core.Connections;
using Otools.Net.Sfe.Core.Files;

namespace Otools.Net.Sfe.Core.Transfers;

/// <summary>
/// One file of a transfer written at the target (<see cref="TransferStream"/>), then verified: its size always, its
/// content hash when either side gives one (<see cref="TransferHashes"/>): the source's before the file is committed
/// (nothing is left), the target's after (the written file is removed). The item fails, so a move keeps its source.
/// </summary>
internal sealed class TransferFileWriter(JobRun job, ITransferEndpoint source, ITransferEndpoint target)
{
    /// <summary>The parts read and written in one go: at most the target's largest part.</summary>
    private int PartSize =>
        target is RemoteTransferEndpoint { MaxUploadPart: { } most and > 0 }
            ? (int)Math.Min(TransferStream.MaxPart, most)
            : TransferStream.MaxPart;

    /// <summary>
    /// Writes <paramref name="file"/> as <paramref name="name"/> into <paramref name="folder"/> and answers it as
    /// written; a <see cref="RemoteStorageException"/> when it failed or was not kept.
    /// </summary>
    public async Task<TransferEntry> WriteAsync(TransferEntry file, string folder, string name, CancellationToken token)
    {
        var size = file.Size
            ?? throw new RemoteStorageException(RemoteErrorKind.NotSupported, $"The size of “{file.Name}” is unknown.");
        using var hashes = new TransferHashes(file, source.HashName, target.HashName);
        var written = await StreamAsync(file, (folder, name, size), hashes, token);
        var problem =
            written.Size is { } arrived && arrived != size ? "its size differs" : hashes.TargetMismatch(written);
        if (problem is null)
            return written;

        await RemoveAsync(written);
        throw new RemoteStorageException(RemoteErrorKind.Unavailable, $"“{file.Name}” was not kept: {problem}.");
    }

    private async Task<TransferEntry> StreamAsync(
        TransferEntry file,
        (string Folder, string Name, long Size) to,
        TransferHashes hashes,
        CancellationToken token
    )
    {
        await using var write = await target.BeginWriteAsync(to.Folder, to.Name, to.Size, file.Modified, token);
        await using var stream = new TransferStream(job, source, file, hashes);
        try
        {
            await CopyCheckedAsync(stream, write, to.Size, token);
        }
        catch
        {
            // A partial file goes, an upload session is left to expire.
            await write.AbortAsync(CancellationToken.None);
            throw;
        }
        return await write.CommitAsync(token);
    }

    /// <summary>Streams the file; what was read must match the source's hash before it is committed.</summary>
    private async Task CopyCheckedAsync(TransferStream stream, ITransferWrite write, long size, CancellationToken token)
    {
        await stream.CopyAsync(write, size, PartSize, token);
        if (stream.Hashes.SourceMismatch() is { } problem)
            throw new RemoteStorageException(RemoteErrorKind.Unavailable, $"“{stream.Name}” was not kept: {problem}.");
    }

    /// <summary>The written file that failed its check goes (to the target's trash when it has one).</summary>
    private async Task RemoveAsync(TransferEntry written)
    {
        try
        {
            await target.RemoveForReplaceAsync(written, CancellationToken.None);
        }
        catch (RemoteStorageException)
        {
            // Left where it is: the item's failure says it was not kept.
        }
    }
}
