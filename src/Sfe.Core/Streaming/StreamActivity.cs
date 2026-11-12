namespace Sfe.Core.Streaming;

/// <summary>A video being converted for a viewer.</summary>
/// <param name="Path">The video, relative to its library's root.</param>
/// <param name="Start">Position in the video (seconds) the conversion started from.</param>
/// <param name="Transcoded">The pictures are encoded again (false: H.264 copied, only repackaged).</param>
/// <param name="Bytes">Sent to the viewer so far.</param>
public sealed record StreamActivity(
    Guid Id,
    string Path,
    StreamViewer Viewer,
    double Start,
    bool Transcoded,
    DateTimeOffset Started,
    long Bytes
);
