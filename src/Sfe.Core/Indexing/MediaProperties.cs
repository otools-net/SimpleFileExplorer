namespace Sfe.Core.Indexing;

/// <summary>What a scan reads of an image, video or audio file.</summary>
/// <param name="Size">The picture of an image or video; null for audio, which has none (a cover is not its
/// picture).</param>
/// <param name="Duration">Seconds of a video or audio file; 0 when ffmpeg does not tell (a broken file); null for
/// images.</param>
public readonly record struct MediaProperties(PixelSize? Size, double? Duration = null);
