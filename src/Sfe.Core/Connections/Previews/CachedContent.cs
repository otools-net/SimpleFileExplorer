using Otools.Net.Sfe.Core.Library;

namespace Otools.Net.Sfe.Core.Connections.Previews;

/// <summary>
/// A remote file whole in the connections' cache, held there (never evicted) until disposed. Made only by
/// <see cref="ConnectionContentCache"/>: its path is the cache's, never one a request named.
/// </summary>
public sealed class CachedContent : IDisposable
{
    private readonly ConnectionCacheStore _store;
    private int _released;

    internal CachedContent(ConnectionCacheStore store, string fullPath, RemoteCacheKey key)
    {
        _store = store;
        FullPath = fullPath;
        Key = key;
        Size = new FileInfo(fullPath).Length;
        Media = MediaSniffer.DetectFile(fullPath);
    }

    /// <summary>The cache's folder (<see cref="ConnectionCacheStore.Folder"/>) the file is in.</summary>
    public string Root => _store.Folder;

    public string FullPath { get; }

    public RemoteCacheKey Key { get; }

    public long Size { get; }

    /// <summary>The kind of media its bytes are (<see cref="MediaSniffer"/>); null when they are none.</summary>
    public ItemKind? Media { get; }

    public void Dispose()
    {
        if (Interlocked.Exchange(ref _released, 1) == 0)
            _store.Release(FullPath);
    }
}
