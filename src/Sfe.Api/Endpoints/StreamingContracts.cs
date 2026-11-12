using Sfe.Core.Options;
using Sfe.Core.Streaming;

namespace Sfe.Api.Endpoints;

/// <param name="Duration">The video's length in seconds (the stream itself has none while it is made); null when
/// unknown.</param>
/// <param name="Transcoded">Its pictures are encoded again (false: copied, which is lighter but seeks to key
/// frames).</param>
public sealed record StreamProbe(double? Duration, bool Transcoded);

/// <param name="Available">ffmpeg was found and streaming is on in this library.</param>
/// <param name="Mode">When this library converts videos.</param>
/// <param name="MaxStreams">Videos the whole server converts at once.</param>
/// <param name="Streams">Running now, in every library, oldest first.</param>
public sealed record StreamingStatus(
    bool Available,
    StreamingMode Mode,
    int MaxStreams,
    IReadOnlyList<StreamActivity> Streams
);
