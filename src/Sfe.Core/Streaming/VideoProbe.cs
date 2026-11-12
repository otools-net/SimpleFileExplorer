namespace Sfe.Core.Streaming;

/// <summary>What ffmpeg tells about a video, to convert it.</summary>
/// <param name="Duration">Seconds; null when the container doesn't say.</param>
/// <param name="VideoCodec">The first picture stream's codec (<c>h264</c>, <c>hevc</c>...), null without
/// pictures.</param>
/// <param name="BrowserPictures">That stream is H.264 in a pixel format browsers play (8-bit 4:2:0), so it can be
/// copied.</param>
/// <param name="AudioCodec">The first sound stream's codec (<c>aac</c>, <c>ac3</c>...), null without sound.</param>
public sealed record VideoProbe(double? Duration, string? VideoCodec, bool BrowserPictures, string? AudioCodec);
