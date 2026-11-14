using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using Sfe.Core.Connections;
using Sfe.Core.Files;

namespace Sfe.Core.Transfers;

/// <summary>
/// A connection's side of a transfer: <see cref="IRemoteStorage"/> through <see cref="TransferCalls"/> (waits
/// instead of failing on the provider's limits, retries, one write at a time). Deletes go to the provider's own
/// trash; what was written drops the connection's listing snapshots. Disposing it disposes the storage.
/// </summary>
public sealed class RemoteTransferEndpoint(
    int connectionId,
    IRemoteStorage storage,
    ProviderCapabilities capabilities,
    TransferCalls calls,
    RemoteListings listings
) : ITransferEndpoint
{
    private const int SkipBuffer = 81920;

    public string? HashName => capabilities.ContentHash;

    /// <summary>The largest part the provider takes in one call; null when it sets no limit.</summary>
    internal long? MaxUploadPart => capabilities.MaxUploadPart;

    public async Task<TransferEntry?> GetAsync(string path, CancellationToken cancellationToken)
    {
        var item = await calls.RunAsync(connectionId, token => storage.GetAsync(path, token), cancellationToken);
        return item is null ? null : EntryOf(item);
    }

    public async IAsyncEnumerable<TransferEntry> ListAsync(
        string folder,
        [EnumeratorCancellation] CancellationToken cancellationToken
    )
    {
        string? cursor = null;
        do
        {
            var from = cursor;
            var page = await calls.RunAsync(
                connectionId,
                token => storage.ListAsync(folder, from, token),
                cancellationToken
            );
            foreach (var item in page.Items)
                yield return EntryOf(item);
            cursor = page.Cursor;
        } while (cursor is not null);
    }

    public async Task<Stream> OpenReadAsync(TransferEntry file, long offset, CancellationToken cancellationToken)
    {
        var range = offset > 0 && capabilities.RangedReads ? new RemoteRange(offset) : null;
        var content = await calls.RunAsync(
            connectionId,
            token => storage.OpenReadAsync(file.Path, range, token),
            cancellationToken
        );
        var served = content.Range?.From ?? 0;
        if (served == offset)
            return content.Stream;
        try
        {
            if (served > offset)
                throw new RemoteStorageException(RemoteErrorKind.Unavailable, "The provider served another part.");
            await SkipAsync(content.Stream, offset - served, cancellationToken);
            return content.Stream;
        }
        catch
        {
            await content.DisposeAsync();
            throw;
        }
    }

    public async Task<ITransferWrite> BeginWriteAsync(
        string folder,
        string name,
        long size,
        DateTime? modified,
        CancellationToken cancellationToken
    )
    {
        var upload = await calls.RunAsync(connectionId, storage.BeginUploadAsync, cancellationToken);
        DateTimeOffset? stamp = modified is { } date ? new(DateTime.SpecifyKind(date, DateTimeKind.Utc)) : null;
        var target = new RemoteUploadTarget(RemotePaths.Combine(folder, name), size, RemoteConflict.Fail, stamp);
        return new RemoteTransferWrite(this, upload, target);
    }

    public async Task<TransferEntry> CreateFolderAsync(string folder, string name, CancellationToken cancellationToken)
    {
        var path = RemotePaths.Combine(folder, name);
        try
        {
            var made = await calls.WriteAsync(
                connectionId,
                token => storage.CreateFolderAsync(path, token),
                cancellationToken
            );
            listings.Forget(connectionId);
            return EntryOf(made);
        }
        catch (RemoteStorageException failure) when (failure.Kind == RemoteErrorKind.Exists)
        {
            if (await GetAsync(path, cancellationToken) is { IsFolder: true } existing)
                return existing;
            throw;
        }
    }

    public async Task<string> FreeNameAsync(
        string folder,
        string name,
        bool isFolder,
        CancellationToken cancellationToken
    )
    {
        foreach (var candidate in Candidates(name, isFolder))
        {
            // The provider finds a name by its own rule for case (CaseInsensitiveNames): "a.txt" meets "A.TXT".
            if (await GetAsync(RemotePaths.Combine(folder, candidate), cancellationToken) is null)
                return candidate;
        }
        throw new RemoteStorageException(RemoteErrorKind.Exists, $"No free name for '{name}'.");
    }

    public Task RemoveForReplaceAsync(TransferEntry existing, CancellationToken cancellationToken) =>
        DeleteAsync(existing.Path, cancellationToken);

    public Task DeleteSourceAsync(TransferEntry entry, CancellationToken cancellationToken) =>
        DeleteAsync(entry.Path, cancellationToken);

    public async Task<TransferSpace> FreeSpaceAsync(string folder, CancellationToken cancellationToken)
    {
        if (!capabilities.FreeSpace)
            return new TransferSpace(null);
        var space = await calls.RunAsync(connectionId, storage.SpaceAsync, cancellationToken);
        return new TransferSpace(space?.Total is { } total ? Math.Max(0, total - space.Used) : null);
    }

    public Task AfterWriteAsync(TransferEntry written, CancellationToken cancellationToken)
    {
        listings.Forget(connectionId);
        return Task.CompletedTask;
    }

    public ValueTask DisposeAsync() => storage.DisposeAsync();

    /// <summary>The next part of an upload, at <paramref name="offset"/>, a fresh stream for each try.</summary>
    internal Task AppendAsync(
        string upload,
        long offset,
        ReadOnlyMemory<byte> part,
        CancellationToken cancellationToken
    ) =>
        calls.RunAsync(
            connectionId,
            token => storage.AppendUploadAsync(upload, offset, StreamOf(part), token),
            cancellationToken
        );

    /// <summary>Makes the file of an upload (never over another item: <see cref="RemoteErrorKind.Exists"/>).</summary>
    internal async Task<TransferEntry> FinishAsync(
        string upload,
        RemoteUploadTarget target,
        CancellationToken cancellationToken
    )
    {
        var item = await calls.WriteAsync(
            connectionId,
            token => storage.FinishUploadAsync(upload, target, token),
            cancellationToken
        );
        return EntryOf(item);
    }

    private async Task DeleteAsync(string path, CancellationToken cancellationToken)
    {
        await calls.WriteAsync(connectionId, token => storage.DeleteAsync(path, token), cancellationToken);
        listings.Forget(connectionId);
    }

    /// <summary>The name, then <c>name (2).ext</c>, <c>name (3).ext</c>… as the library numbers them.</summary>
    private static IEnumerable<string> Candidates(string name, bool isFolder)
    {
        yield return name;
        var dot = isFolder ? -1 : name.LastIndexOf('.');
        // A name that is only an extension (".profile") has no stem to number.
        var stem = dot > 0 ? name[..dot] : name;
        var extension = name[stem.Length..];
        for (var attempt = 2; attempt <= FileNames.MaxKeepBothAttempts; attempt++)
            yield return $"{stem} ({attempt}){extension}";
    }

    private static async Task SkipAsync(Stream stream, long count, CancellationToken cancellationToken)
    {
        var buffer = new byte[(int)Math.Min(SkipBuffer, count)];
        while (count > 0)
        {
            var read = await stream.ReadAsync(
                buffer.AsMemory(0, (int)Math.Min(buffer.Length, count)),
                cancellationToken
            );
            if (read == 0)
                return;
            count -= read;
        }
    }

    /// <summary>The part as a stream to read, without copying it when it is an array's.</summary>
    private static MemoryStream StreamOf(ReadOnlyMemory<byte> part) =>
        MemoryMarshal.TryGetArray(part, out var segment)
            ? new MemoryStream(segment.Array!, segment.Offset, segment.Count, writable: false)
            : new MemoryStream(part.ToArray(), writable: false);

    private static TransferEntry EntryOf(RemoteItem item) =>
        new(item.Name, item.Path, item.IsFolder, item.Size, item.Modified, item.Hash, item.Downloadable);
}
