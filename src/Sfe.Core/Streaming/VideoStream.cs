namespace Otools.Net.Sfe.Core.Streaming;

/// <summary>
/// A video ready to be converted (<see cref="VideoStreamer.TryBegin"/>): run it with
/// <see cref="VideoStreamer.RunAsync"/>.
/// </summary>
/// <param name="Cached">A connection's file in the connections' cache, held until the stream ends; null for a file
/// of the library.</param>
public sealed record VideoStream(
    string FullPath,
    double Start,
    bool Transcoded,
    VideoStreams.Lease Lease,
    Connections.Previews.CachedContent? Cached = null
);
