namespace Otools.Net.Sfe.Core.Connections;

/// <summary>
/// What a provider (or the content cache) tells of a picture, video or audio file: its size in pixels and its length.
/// Each value is null when not told.
/// </summary>
/// <param name="Duration">Seconds of a video or audio file.</param>
public sealed record RemoteMedia(int? Width, int? Height, double? Duration)
{
    /// <summary>Something is told.</summary>
    public bool IsKnown => Width is not null || Height is not null || Duration is not null;
}
