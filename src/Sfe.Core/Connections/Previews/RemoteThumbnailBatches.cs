using System.Diagnostics;
using Microsoft.Extensions.Logging;

namespace Otools.Net.Sfe.Core.Connections.Previews;

/// <summary>
/// Thumbnails asked of a connection's provider together: a listing shows dozens of pictures, each asked by its own
/// request; those arriving within <see cref="Gather"/> of each other (up to <see cref="MaxBatch"/>, Dropbox's limit)
/// go in one call (<see cref="IRemoteStorage.ThumbnailsAsync"/>), in one turn of the connection's gate. The batch
/// opens the connection itself, so that a request leaving doesn't end it for the others. Access is the routes'.
/// </summary>
public sealed class RemoteThumbnailBatches(
    ConnectionService connections,
    ConnectionGates gates,
    ILogger<RemoteThumbnailBatches> logger
)
{
    /// <summary>Thumbnails in one call to the provider (Dropbox: 25).</summary>
    public const int MaxBatch = 25;

    /// <summary>How long a batch waits for more requests before it is sent.</summary>
    public static readonly TimeSpan Gather = TimeSpan.FromMilliseconds(30);

    private readonly Lock _gate = new();
    private readonly Dictionary<(int Connection, int Size), Batch> _open = [];

    /// <summary>The provider's thumbnail of the file at <paramref name="path"/>; null when it makes none.</summary>
    /// <exception cref="RemoteStorageException">The provider failed, or asks to wait.</exception>
    public async Task<RemoteThumbnail?> GetAsync(
        int connectionId,
        string path,
        int size,
        CancellationToken cancellationToken
    )
    {
        var place = (connectionId, size);
        Batch batch;
        int index;
        var start = false;
        lock (_gate)
        {
            if (!_open.TryGetValue(place, out batch!))
            {
                _open[place] = batch = new Batch();
                start = true;
            }
            index = batch.Add(path);
            if (batch.Paths.Count >= MaxBatch)
            {
                _open.Remove(place);
                batch.Full.TrySetResult();
            }
        }
        if (start)
            _ = Task.Run(() => RunAsync(place, batch));
        var thumbnails = await batch.Done.Task.WaitAsync(cancellationToken);
        return thumbnails[index];
    }

    private async Task RunAsync((int Connection, int Size) place, Batch batch)
    {
        await Task.WhenAny(batch.Full.Task, Task.Delay(Gather));
        List<string> paths;
        lock (_gate)
        {
            if (_open.TryGetValue(place, out var open) && open == batch)
                _open.Remove(place);
            paths = [.. batch.Paths];
        }
        try
        {
            batch.Done.TrySetResult(await CallAsync(place.Connection, paths, place.Size));
        }
        catch (Exception error)
        {
            batch.Done.TrySetException(error);
        }
    }

    private async Task<IReadOnlyList<RemoteThumbnail?>> CallAsync(int connectionId, List<string> paths, int size)
    {
        if (gates.WaitLeft(connectionId) is { } left)
            throw new RemoteStorageException(RemoteErrorKind.RateLimited, "The storage service asks to wait.", left);
        var opened = await connections.OpenAsync(connectionId);
        await using var storage = opened.Value
            ?? throw new RemoteStorageException(RemoteErrorKind.Unavailable, "The connection can't be opened.");
        using var turn = await gates.EnterAsync(connectionId, CancellationToken.None)
            ?? throw new RemoteStorageException(RemoteErrorKind.RateLimited, "The connection is busy.");
        try
        {
            var started = Stopwatch.GetTimestamp();
            var thumbnails = await storage.ThumbnailsAsync(paths, size, CancellationToken.None);
            // One line per provider call: the dropbox-check guide counts them (a folder of 100 photos: ~4).
            logger.LogDebug(
                "Connection {ConnectionId}: {Count} thumbnails of {Size} px asked in one call, {Made} made, {Sized} "
                    + "telling the file's size or length, {Ms} ms",
                connectionId,
                paths.Count,
                size,
                thumbnails.Count(t => t is not null),
                thumbnails.Count(t => t?.Media is not null),
                (int)Stopwatch.GetElapsedTime(started).TotalMilliseconds
            );
            return thumbnails.Count == paths.Count
                ? thumbnails
                : [.. paths.Select((_, i) => i < thumbnails.Count ? thumbnails[i] : null)];
        }
        catch (RemoteStorageException error) when (error.Kind == RemoteErrorKind.RateLimited)
        {
            gates.Wait(connectionId, error.RetryAfter);
            throw;
        }
    }

    /// <summary>Paths asked together; one entry per path (two requests for one file share it).</summary>
    private sealed class Batch
    {
        public List<string> Paths { get; } = [];

        public TaskCompletionSource Full { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);

        public TaskCompletionSource<IReadOnlyList<RemoteThumbnail?>> Done { get; } =
            new(TaskCreationOptions.RunContinuationsAsynchronously);

        public int Add(string path)
        {
            var index = Paths.IndexOf(path);
            if (index >= 0)
                return index;
            Paths.Add(path);
            return Paths.Count - 1;
        }
    }
}
