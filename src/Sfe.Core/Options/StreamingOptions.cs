namespace Sfe.Core.Options;

/// <summary>
/// Videos converted by ffmpeg while they play, for browsers that cannot play the file itself (AVI, WMV, HEVC...): H.264
/// and AAC in fragmented MP4, made from the position asked for, so that seeking starts a new conversion there. Needs
/// ffmpeg (<see cref="ThumbnailOptions.FfmpegPath"/>).
/// </summary>
public sealed class StreamingOptions
{
    public StreamingMode Mode { get; set; } = StreamingMode.Fallback;

    /// <summary>
    /// Videos converted at once on the whole server: each one keeps a processor core or more busy. A viewer asking for
    /// one more is refused until one ends.
    /// </summary>
    public int MaxStreams { get; set; } = 2;

    /// <summary>
    /// x264's constant rate factor: lower is better and heavier (18 looks like the original, 28 is small).
    /// </summary>
    public int Quality { get; set; } = 23;

    /// <summary>
    /// Converted videos taller than this are scaled down to it, keeping their shape; 0 keeps their size.
    /// </summary>
    public int MaxHeight { get; set; } = 1080;

    /// <summary>
    /// H.264 pictures that browsers can play (8-bit 4:2:0) are copied as they are, only repackaged with converted
    /// sound: almost no processor time, but seeking lands on the nearest earlier key frame, and the size limit doesn't
    /// apply.
    /// </summary>
    public bool CopyVideo { get; set; } = true;
}
