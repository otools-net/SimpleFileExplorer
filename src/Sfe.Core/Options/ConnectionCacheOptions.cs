namespace Otools.Net.Sfe.Core.Options;

/// <summary>
/// The content cache of connections (<c>Sfe:Connections:Cache</c>, agents/docs/features/connection-previews.md):
/// files of a connection downloaded once for what reads them whole on the server (ffmpeg: video conversion, pictures
/// the provider doesn't thumbnail, sizes and lengths), with the thumbnails the providers made. One store for the whole
/// server, under the thumbnail cache folder (<c>{Thumbnails:CacheDirectory}/connections</c>); server settings.
/// </summary>
public sealed class ConnectionCacheOptions
{
    /// <summary>Megabytes the whole cache may hold; the files used longest ago go first. 0 turns it off.</summary>
    public int MaxSizeMegabytes { get; set; } = 4096;

    /// <summary>A larger file is never downloaded into it (it plays only when the browser plays it).</summary>
    public int MaxFileMegabytes { get; set; } = 2048;

    /// <summary>Days a file or thumbnail nobody used stays.</summary>
    public int MaxAgeDays { get; set; } = 30;
}
