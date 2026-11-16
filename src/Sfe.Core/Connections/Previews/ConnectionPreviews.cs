using System.Collections.Concurrent;
using Microsoft.Extensions.Logging;
using Otools.Net.Sfe.Core.Connections.Indexing;
using Otools.Net.Sfe.Core.Indexing;
using Otools.Net.Sfe.Core.Library;
using Otools.Net.Sfe.Core.Thumbnails;

namespace Otools.Net.Sfe.Core.Connections.Previews;

/// <summary>
/// Previews of a connection's files for one library (agents/docs/features/connection-previews.md): thumbnails from the
/// server's cache, else from the provider (asked in batches), else, for a picture the provider doesn't thumbnail, made
/// by ffmpeg from the file downloaded into the content cache; a video only from a copy already there (played). What
/// the provider or ffmpeg tells of a file (size in pixels, length) is kept beside it and written on the library's row.
/// The access to the file is the caller's to check first.
/// </summary>
public sealed class ConnectionPreviews(
    ConnectionThumbnailStore thumbnails,
    RemoteThumbnailBatches batches,
    ConnectionContentCache content,
    ThumbnailService renderer,
    MediaDimensions dimensions,
    RemoteMediaRows rows,
    ILogger<ConnectionPreviews> logger
)
{
    /// <summary>Rows told already (by connection and key), so a listing's thumbnails don't each write.</summary>
    private const int ToldLimit = 10_000;

    private readonly ConcurrentDictionary<string, byte> _told = new(StringComparer.Ordinal);

    /// <summary>
    /// The JPEG thumbnail of the file, about <paramref name="size"/> pixels wide; null when it has none.
    /// </summary>
    /// <exception cref="RemoteStorageException">The provider failed, or asks to wait.</exception>
    public async Task<byte[]?> ThumbnailAsync(PreviewAsk ask, int size, CancellationToken cancellationToken)
    {
        if (ask.Kind is not (ItemKind.Image or ItemKind.Video))
            return null;
        var key = ask.Key;
        size = ConnectionThumbnailStore.SizeOf(size);
        if (thumbnails.Find(ask.ConnectionId, key, size) is { } kept)
        {
            await TellAsync(ask, thumbnails.MediaOf(ask.ConnectionId, key), cancellationToken);
            return kept.Length > 0 ? kept : null;
        }

        var (made, final) = await MakeAsync(ask, size, cancellationToken);
        if (made is not null || final)
            thumbnails.Save(ask.ConnectionId, key, size, made ?? []);
        return made;
    }

    /// <summary>
    /// The file whole in the content cache (downloaded when needed), held until disposed; what it is (size,
    /// length) is read then and told to the library's row.
    /// </summary>
    /// <exception cref="ConnectionCacheException">The cache doesn't take it now.</exception>
    /// <exception cref="RemoteStorageException">The provider failed.</exception>
    public async Task<CachedContent> ContentAsync(PreviewAsk ask, CancellationToken cancellationToken)
    {
        var cached = await content.OpenAsync(ask.Content, cancellationToken);
        await DescribeAsync(ask, cached, cancellationToken);
        return cached;
    }

    /// <summary>
    /// A thumbnail made now: the provider's, else ffmpeg's from the content cache; <c>Final</c> when a missing one
    /// is missing for good (not a passing refusal of the cache, nor ffmpeg missing).
    /// </summary>
    private async Task<(byte[]? Bytes, bool Final)> MakeAsync(PreviewAsk ask, int size, CancellationToken token)
    {
        if (ask.Capabilities.Thumbnails)
        {
            var made = await batches.GetAsync(ask.ConnectionId, ask.Item.Path, size, token);
            if (made is not null)
            {
                await KeepMediaAsync(ask, made.Media, token);
                return (made.Bytes, true);
            }
        }
        if (!renderer.IsAvailable)
            return (null, false);
        return await FromContentAsync(ask, token);
    }

    /// <summary>A picture downloaded into the cache, or a video already there, rendered by ffmpeg.</summary>
    private async Task<(byte[]? Bytes, bool Final)> FromContentAsync(PreviewAsk ask, CancellationToken token)
    {
        CachedContent? cached;
        try
        {
            cached = ask.Kind == ItemKind.Image ? await ContentAsync(ask, token) : content.Find(ask.Content);
        }
        catch (ConnectionCacheException refused)
        {
            return (null, refused.Reason is CacheRefusal.TooLarge or CacheRefusal.NoContent);
        }
        if (cached is null)
            return (null, false);
        using (cached)
        {
            if (ask.Kind == ItemKind.Video)
                await DescribeAsync(ask, cached, token);
            var image = await renderer.RenderCachedAsync(cached, ask.Kind, token);
            return (image.Length > 0 ? image : null, true);
        }
    }

    /// <summary>What the cached file is: kept beside it once, told to the library's row.</summary>
    private async Task DescribeAsync(PreviewAsk ask, CachedContent cached, CancellationToken token)
    {
        if (thumbnails.MediaOf(ask.ConnectionId, cached.Key) is { } known)
        {
            await TellAsync(ask, known, token);
            return;
        }
        if (cached.Media is null || !dimensions.CanRead(ask.Kind))
            return;
        try
        {
            if (await dimensions.ReadCachedAsync(cached, ask.Kind, token) is { } read)
                await KeepMediaAsync(ask, MediaOf(read), token);
        }
        catch (InvalidOperationException ex)
        {
            logger.LogDebug(ex, "A file of connection {Connection} could not be described", ask.ConnectionId);
        }
    }

    private async Task KeepMediaAsync(PreviewAsk ask, RemoteMedia? media, CancellationToken token)
    {
        if (media is not { IsKnown: true })
            return;
        thumbnails.SaveMedia(ask.ConnectionId, ask.Key, media);
        await TellAsync(ask, media, token);
    }

    /// <summary>Writes what is known on the library's row of the file, once per file and version.</summary>
    private async Task TellAsync(PreviewAsk ask, RemoteMedia? media, CancellationToken token)
    {
        if (media is not { IsKnown: true } || ask.Place is not { } place)
            return;
        var told = $"{ask.ConnectionId}/{ask.Key.Name}";
        if (_told.ContainsKey(told))
            return;
        if (_told.Count >= ToldLimit)
            _told.Clear();
        await rows.FillAsync(place, ask.Item, media, token);
        _told[told] = 0;
    }

    private static RemoteMedia MediaOf(MediaProperties read) =>
        new(read.Size?.Width, read.Size?.Height, read.Duration is > 0 ? read.Duration : null);
}
