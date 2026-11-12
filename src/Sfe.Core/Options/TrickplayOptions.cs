namespace Sfe.Core.Options;

/// <summary>
/// Trickplay: frames of a video, taken at regular intervals and tiled on sheets, that the player shows over its seek
/// bar. Rendered with ffmpeg by a background job and kept under <c>{Thumbnails:CacheDirectory}/trickplay</c>.
/// </summary>
public sealed class TrickplayOptions
{
    /// <summary>Off by default: a first pass over a large library keeps ffmpeg busy for a long time.</summary>
    public bool Enabled { get; set; }

    /// <summary>
    /// Library folders (relative to the root, with their sub-folders) whose videos get trickplay; empty = the whole
    /// library.
    /// </summary>
    public List<string> Folders { get; set; } = [];

    /// <summary>
    /// Library folders (relative to the root, with their sub-folders) whose videos get no trickplay, even inside
    /// <see cref="Folders"/>; sheets already made are still shown.
    /// </summary>
    public List<string> ExcludedFolders { get; set; } = [];

    /// <summary>Seconds between two frames.</summary>
    public int IntervalSeconds { get; set; } = 10;

    /// <summary>Width of a frame in pixels (smaller videos keep their width).</summary>
    public int Width { get; set; } = 320;

    /// <summary>Frames per row of a sheet.</summary>
    public int Columns { get; set; } = 10;

    /// <summary>Rows of frames per sheet.</summary>
    public int Rows { get; set; } = 10;

    /// <summary>
    /// Decode only key frames: many times faster, but a frame can be a few seconds off its position
    /// (the nearest earlier key frame).
    /// </summary>
    public bool KeyFramesOnly { get; set; } = true;

    /// <summary>Videos processed at once by the background job.</summary>
    public int Parallelism { get; set; } = 1;

    /// <summary>
    /// Time limit for one video; a video taking longer is given up on until it or the settings change.
    /// </summary>
    public int TimeoutMinutes { get; set; } = 30;

    /// <summary>
    /// Hours between two passes over the folders looking for new or changed videos; 0 = only at start-up and when
    /// the settings change. A video opened in the player is always handled right away.
    /// </summary>
    public int RecheckHours { get; set; } = 6;

    /// <summary>
    /// Hovering a video in a listing plays its trickplay over the thumbnail. Only shows existing trickplay: it never
    /// queues a video.
    /// </summary>
    public bool HoverPreview { get; set; } = true;
}
