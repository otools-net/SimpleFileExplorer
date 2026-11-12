namespace Sfe.Core.Streaming;

/// <summary>
/// A video ready to be converted (<see cref="VideoStreamer.TryBegin"/>): run it with
/// <see cref="VideoStreamer.RunAsync"/>.
/// </summary>
public sealed record VideoStream(string FullPath, double Start, bool Transcoded, VideoStreams.Lease Lease);
