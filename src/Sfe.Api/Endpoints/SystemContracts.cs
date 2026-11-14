namespace Otools.Net.Sfe.Api.Endpoints;

/// <param name="Configured">The executable as set in <c>Thumbnails:FfmpegPath</c> (a path, or a name looked up on
/// PATH).</param>
/// <param name="Path">Where it was found; null when it wasn't (thumbnails of videos, trickplay and streaming are
/// off).</param>
/// <param name="Version">The version it reports, when found.</param>
public sealed record FfmpegInfo(string Configured, string? Path, string? Version);

/// <param name="Root">Full path of the library root on the server.</param>
/// <param name="Ffmpeg">The ffmpeg executable thumbnails, trickplay and streaming run.</param>
public sealed record SystemInfo(string Root, FfmpegInfo Ffmpeg);
