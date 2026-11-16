using Dropbox.Api;
using Dropbox.Api.Files;

namespace Otools.Net.Sfe.Core.Connections.Dropbox;

/// <summary>
/// Dropbox's thumbnails (agents/docs/features/connection-previews.md): one file through <c>get_thumbnail</c>, which
/// also tells the file's <c>media_info</c> (size in pixels, a video's length), several through
/// <c>get_thumbnail_batch</c> (at most <see cref="MaxBatch"/> a call; Dropbox stopped giving <c>media_info</c> there).
/// Pictures only by Dropbox's documentation (jpg, jpeg, png, tiff, tif, gif, webp, ppm, bmp), none over 20 MB: a file
/// it doesn't thumbnail has none (null), never an error.
/// </summary>
internal static class DropboxThumbnails
{
    /// <summary>Thumbnails Dropbox makes in one call (<c>too_many_files</c> above).</summary>
    public const int MaxBatch = 25;

    public static async Task<IReadOnlyList<RemoteThumbnail?>> GetAsync(
        DropboxClient client,
        IReadOnlyList<string> dropboxPaths,
        int size
    )
    {
        if (dropboxPaths.Count == 1)
            return [await OneAsync(client, dropboxPaths[0], size)];

        var thumbnails = new List<RemoteThumbnail?>(dropboxPaths.Count);
        foreach (var batch in dropboxPaths.Chunk(MaxBatch))
        {
            var asked = batch.Select(path => new ThumbnailArg(path, ThumbnailFormat.Jpeg.Instance, SizeOf(size)));
            var result = await DropboxErrors.CallAsync(() => client.Files.GetThumbnailBatchAsync(asked));
            thumbnails.AddRange(result.Entries.Select(ThumbnailOf));
            // An answer with fewer entries than asked: the missing ones have none.
            thumbnails.AddRange(Enumerable.Repeat<RemoteThumbnail?>(null, batch.Length - result.Entries.Count));
        }
        return thumbnails;
    }

    /// <summary>One thumbnail, with what Dropbox tells of the file (its <c>media_info</c>).</summary>
    public static async Task<RemoteThumbnail?> OneAsync(DropboxClient client, string dropboxPath, int size)
    {
        try
        {
            using var answer = await DropboxErrors.CallAsync(() =>
                client.Files.GetThumbnailAsync(dropboxPath, ThumbnailFormat.Jpeg.Instance, SizeOf(size))
            );
            var bytes = await answer.GetContentAsByteArrayAsync();
            return new RemoteThumbnail(bytes, "image/jpeg", MediaOf(answer.Response));
        }
        catch (RemoteStorageException error) when (error.Kind is RemoteErrorKind.Refused or RemoteErrorKind.NotFound)
        {
            // Not a file Dropbox makes thumbnails of (unsupported_extension, unsupported_image, conversion_error).
            return null;
        }
    }

    /// <summary>The size Dropbox makes closest to <paramref name="size"/> pixels wide (at least as wide).</summary>
    public static ThumbnailSize SizeOf(int size) =>
        size switch
        {
            <= 64 => ThumbnailSize.W64h64.Instance,
            <= 128 => ThumbnailSize.W128h128.Instance,
            <= 256 => ThumbnailSize.W256h256.Instance,
            <= 640 => ThumbnailSize.W640h480.Instance,
            _ => ThumbnailSize.W1024h768.Instance,
        };

    /// <summary>The size and length Dropbox tells of a picture or video; null when it doesn't (or not yet).</summary>
    internal static RemoteMedia? MediaOf(FileMetadata? file)
    {
        if (file?.MediaInfo is not { IsMetadata: true } info)
            return null;
        var metadata = info.AsMetadata.Value;
        var media = new RemoteMedia(
            metadata.Dimensions is { } width ? (int)Math.Min(width.Width, int.MaxValue) : null,
            metadata.Dimensions is { } height ? (int)Math.Min(height.Height, int.MaxValue) : null,
            metadata.IsVideo && metadata.AsVideo.Duration is { } milliseconds ? milliseconds / 1000.0 : null
        );
        return media.IsKnown ? media : null;
    }

    private static RemoteThumbnail? ThumbnailOf(GetThumbnailBatchResultEntry entry)
    {
        if (!entry.IsSuccess || entry.AsSuccess.Value.Thumbnail is not { Length: > 0 } encoded)
            return null;
        try
        {
            var data = entry.AsSuccess.Value;
            return new RemoteThumbnail(Convert.FromBase64String(encoded), "image/jpeg", MediaOf(data.Metadata));
        }
        catch (FormatException)
        {
            // An answer of another shape: no thumbnail rather than a failure of the whole batch.
            return null;
        }
    }
}
