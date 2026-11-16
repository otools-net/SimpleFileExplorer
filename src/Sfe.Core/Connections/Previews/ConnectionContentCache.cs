using Microsoft.Extensions.Logging;

namespace Otools.Net.Sfe.Core.Connections.Previews;

/// <summary>
/// Files of connections downloaded whole into the server's cache (<see cref="ConnectionCacheStore"/>) for what must
/// read them on the server: ffmpeg (a video converted while it plays, a picture the provider doesn't thumbnail, sizes
/// and lengths). One download per file at a time, whoever asks; at most <see cref="DownloadsPerCaller"/> per caller and
/// <see cref="MaxDownloads"/> for the server; none over <see cref="Options.ConnectionCacheOptions.MaxFileMegabytes"/>.
/// The cache never decides access: the routes check the connection and its folder rules first, and give the item the
/// provider told them (its id and version make the key). A download nobody waits for any more stops.
/// </summary>
public sealed class ConnectionContentCache(
    ConnectionCacheStore store,
    ConnectionService connections,
    ConnectionGates gates,
    ILogger<ConnectionContentCache> logger
)
{
    /// <summary>Files one caller (an account, a network address) may have downloading at once.</summary>
    public const int DownloadsPerCaller = 2;

    /// <summary>Files the whole server downloads at once.</summary>
    public const int MaxDownloads = 4;

    private const string Suffix = ".bin";

    private readonly Lock _gate = new();
    private readonly Dictionary<string, Download> _downloads = new(StringComparer.Ordinal);
    private readonly Dictionary<string, int> _callers = new(StringComparer.Ordinal);

    /// <summary>Files being downloaded now.</summary>
    public int Downloading
    {
        get
        {
            lock (_gate)
                return _downloads.Count;
        }
    }

    /// <summary>
    /// The file whole in the cache, held until the answer is disposed: from the cache, else downloaded (or waited for
    /// when another request downloads it).
    /// </summary>
    /// <exception cref="ConnectionCacheException">The cache doesn't take it now.</exception>
    /// <exception cref="RemoteStorageException">The provider failed.</exception>
    public async Task<CachedContent> OpenAsync(ContentAsk ask, CancellationToken cancellationToken)
    {
        if (!store.IsOn)
            throw new ConnectionCacheException(CacheRefusal.Off);
        if (ask.Item.IsFolder || !ask.Item.Downloadable)
            throw new ConnectionCacheException(CacheRefusal.NoContent);
        if (ask.Item.Size > store.MaxFileBytes)
            throw new ConnectionCacheException(CacheRefusal.TooLarge);

        var key = RemoteCacheKey.Of(ask.Item, ask.CaseInsensitive);
        var path = store.PathOf(ask.ConnectionId, key, Suffix);
        if (TryHold(path, key) is { } cached)
            return cached;

        var download = Join(ask, path);
        try
        {
            await download.Done.Task.WaitAsync(cancellationToken);
        }
        finally
        {
            Leave(download);
        }
        return TryHold(path, key) ?? throw new ConnectionCacheException(CacheRefusal.NoContent);
    }

    /// <summary>The file held when it is in the cache already; null when not (nothing is downloaded).</summary>
    public CachedContent? Find(ContentAsk ask)
    {
        if (!store.IsOn)
            return null;
        var key = RemoteCacheKey.Of(ask.Item, ask.CaseInsensitive);
        return TryHold(store.PathOf(ask.ConnectionId, key, Suffix), key);
    }

    /// <summary>The file held when it is in the cache; null when not.</summary>
    private CachedContent? TryHold(string path, RemoteCacheKey key)
    {
        store.Hold(path);
        try
        {
            if (!File.Exists(path))
            {
                store.Release(path);
                return null;
            }
            store.Touch(path);
            return new CachedContent(store, path, key);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            store.Release(path);
            return null;
        }
    }

    /// <summary>The download of <paramref name="path"/>, started when nobody downloads it yet.</summary>
    private Download Join(ContentAsk ask, string path)
    {
        Download download;
        lock (_gate)
        {
            // A download stopped as its last waiter left is not joined: a new one starts.
            if (!_downloads.TryGetValue(path, out download!) || download.Stop.IsCancellationRequested)
            {
                if (_callers.GetValueOrDefault(ask.Caller) >= DownloadsPerCaller || _downloads.Count >= MaxDownloads)
                    throw new ConnectionCacheException(CacheRefusal.Busy);
                download = new Download(ask.Caller);
                _downloads[path] = download;
                _callers[ask.Caller] = _callers.GetValueOrDefault(ask.Caller) + 1;
                _ = Task.Run(() => RunAsync(download, ask, path));
            }
            download.Waiters++;
        }
        return download;
    }

    /// <summary>One waiter less: the download stops when nobody waits for it any more.</summary>
    private void Leave(Download download)
    {
        lock (_gate)
        {
            if (--download.Waiters == 0 && !download.Done.Task.IsCompleted)
                download.Stop.Cancel();
        }
    }

    private async Task RunAsync(Download download, ContentAsk ask, string path)
    {
        try
        {
            await DownloadAsync(ask, path, download.Stop.Token);
            store.ForgetOtherVersions(ask.ConnectionId, RemoteCacheKey.Of(ask.Item, ask.CaseInsensitive));
            download.Done.TrySetResult();
        }
        catch (Exception ex)
        {
            if (ex is not (OperationCanceledException or RemoteStorageException or ConnectionCacheException))
                logger.LogWarning(ex, "A file of connection {Connection} could not be cached", ask.ConnectionId);
            download.Done.TrySetException(ex);
        }
        finally
        {
            lock (_gate)
            {
                if (_downloads.TryGetValue(path, out var current) && current == download)
                    _downloads.Remove(path);
                if (_callers.GetValueOrDefault(download.Caller) <= 1)
                    _callers.Remove(download.Caller);
                else
                    _callers[download.Caller]--;
            }
        }
    }

    /// <summary>The file read from the provider (one turn of its gate) into a part, then put in place.</summary>
    private async Task DownloadAsync(ContentAsk ask, string path, CancellationToken cancellationToken)
    {
        store.MakeRoom(ask.Item.Size ?? 0);
        var opened = await connections.OpenAsync(ask.ConnectionId, cancellationToken);
        await using var storage = opened.Value ?? throw new ConnectionCacheException(CacheRefusal.NoContent);
        await using var content = await ReadAsync(storage, ask, cancellationToken);
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        var part = $"{path}.{Guid.NewGuid():N}{ConnectionCacheStore.PartSuffix}";
        try
        {
            var written = await CopyAsync(content.Stream, part, MaxBytesOf(ask.Item), cancellationToken);
            if (ask.Item.Size is { } expected && written != expected)
                throw new ConnectionCacheException(CacheRefusal.NoContent);
            File.Move(part, path, overwrite: true);
            store.Added(written);
            logger.LogDebug("{Bytes} bytes of connection {Connection} cached", written, ask.ConnectionId);
        }
        finally
        {
            File.Delete(part);
        }
    }

    /// <summary>Opens the read in a turn of the connection's gate (given back once it answers).</summary>
    private async Task<RemoteContent> ReadAsync(IRemoteStorage storage, ContentAsk ask, CancellationToken token)
    {
        if (gates.WaitLeft(ask.ConnectionId) is { } left)
            throw new RemoteStorageException(RemoteErrorKind.RateLimited, "The provider asks to wait.", left);
        using var turn = await gates.EnterAsync(ask.ConnectionId, token)
            ?? throw new RemoteStorageException(RemoteErrorKind.RateLimited, "The connection is busy.");
        try
        {
            return await storage.OpenReadAsync(ask.Item.Path, null, token);
        }
        catch (RemoteStorageException error) when (error.Kind == RemoteErrorKind.RateLimited)
        {
            gates.Wait(ask.ConnectionId, error.RetryAfter);
            throw;
        }
    }

    /// <summary>The bytes a download may write: the size told (a file growing meanwhile is another version).</summary>
    private long MaxBytesOf(RemoteItem item) => Math.Min(item.Size ?? long.MaxValue, store.MaxFileBytes);

    private static async Task<long> CopyAsync(Stream source, string part, long maxBytes, CancellationToken token)
    {
        await using var target = new FileStream(part, FileMode.CreateNew, FileAccess.Write, FileShare.None);
        var buffer = new byte[81920];
        long written = 0;
        int read;
        while ((read = await source.ReadAsync(buffer, token)) > 0)
        {
            written += read;
            if (written > maxBytes)
                throw new ConnectionCacheException(CacheRefusal.TooLarge);
            await target.WriteAsync(buffer.AsMemory(0, read), token);
        }
        return written;
    }

    /// <summary>A file being downloaded, and how many requests wait for it.</summary>
    private sealed class Download(string caller)
    {
        public string Caller { get; } = caller;

        /// <summary>Guarded by <c>_gate</c>.</summary>
        public int Waiters;

        public TaskCompletionSource Done { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);

        public CancellationTokenSource Stop { get; } = new();
    }
}
