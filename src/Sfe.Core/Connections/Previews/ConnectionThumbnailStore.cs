using System.Globalization;

namespace Otools.Net.Sfe.Core.Connections.Previews;

/// <summary>
/// Thumbnails of connections' files and what is known of the files (size in pixels, length) in the server's cache
/// (<see cref="ConnectionCacheStore"/>), by connection and <see cref="RemoteCacheKey"/>: <c>{key}.t{size}.jpg</c> (an
/// empty file: the file has none) and <c>{key}.media</c>. Shared by every library using the connection.
/// </summary>
public sealed class ConnectionThumbnailStore(ConnectionCacheStore store)
{
    /// <summary>The sizes kept (pixels wide, at most): a size asked goes to the next one up.</summary>
    private static readonly int[] Sizes = [64, 128, 256, 640, 1024];

    /// <summary>The size kept for <paramref name="asked"/> pixels.</summary>
    public static int SizeOf(int asked) => Sizes.FirstOrDefault(size => asked <= size, Sizes[^1]);

    /// <summary>A thumbnail kept: its bytes (empty: the file has none); null when none is kept.</summary>
    public byte[]? Find(int connectionId, RemoteCacheKey key, int size)
    {
        if (!store.IsOn)
            return null;
        var path = store.PathOf(connectionId, key, ThumbnailSuffix(size));
        try
        {
            var bytes = File.ReadAllBytes(path);
            store.Touch(path);
            return bytes;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return null;
        }
    }

    /// <summary>Keeps a thumbnail; empty bytes: the file has none (not asked again until it changes).</summary>
    public void Save(int connectionId, RemoteCacheKey key, int size, byte[] bytes)
    {
        if (store.IsOn)
            TryWrite(store.PathOf(connectionId, key, ThumbnailSuffix(size)), bytes);
    }

    /// <summary>What is known of the file's version; null when nothing is.</summary>
    public RemoteMedia? MediaOf(int connectionId, RemoteCacheKey key)
    {
        if (!store.IsOn)
            return null;
        try
        {
            var parts = File.ReadAllText(store.PathOf(connectionId, key, ".media")).Split(' ');
            return parts.Length == 3 ? new RemoteMedia(Int(parts[0]), Int(parts[1]), Seconds(parts[2])) : null;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return null;
        }
    }

    /// <summary>Keeps what is known of the file's version (for every library using the connection).</summary>
    public void SaveMedia(int connectionId, RemoteCacheKey key, RemoteMedia media)
    {
        if (!store.IsOn || !media.IsKnown)
            return;
        var text = string.Join(
            ' ',
            Text(media.Width),
            Text(media.Height),
            media.Duration?.ToString("R", CultureInfo.InvariantCulture) ?? "-"
        );
        TryWrite(store.PathOf(connectionId, key, ".media"), System.Text.Encoding.ASCII.GetBytes(text));
    }

    private void TryWrite(string path, byte[] bytes)
    {
        try
        {
            store.Write(path, bytes);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            // Not kept: made or asked again next time.
        }
    }

    private static string ThumbnailSuffix(int size) => $".t{SizeOf(size)}.jpg";

    private static string Text(int? value) => value?.ToString(CultureInfo.InvariantCulture) ?? "-";

    private static int? Int(string text) =>
        int.TryParse(text, NumberStyles.None, CultureInfo.InvariantCulture, out var value) ? value : null;

    private static double? Seconds(string text) =>
        double.TryParse(text, NumberStyles.Float, CultureInfo.InvariantCulture, out var value) ? value : null;
}
