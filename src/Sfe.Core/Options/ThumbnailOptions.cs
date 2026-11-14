namespace Otools.Net.Sfe.Core.Options;

public sealed class ThumbnailOptions
{
    public bool Enabled { get; set; } = true;

    /// <summary>
    /// Disk cache folder.
    /// Relative paths are resolved against the application content root; a leading <c>~</c> is the home folder.
    /// </summary>
    public string CacheDirectory { get; set; } = "data/thumbs";

    /// <summary>
    /// The full path of a configured <see cref="CacheDirectory"/>, relative ones against <paramref name="basePath"/>.
    /// </summary>
    public static string ResolveCacheDirectory(string cacheDirectory, string basePath) =>
        Path.GetFullPath(Paths.LibraryPaths.ExpandHome(cacheDirectory), basePath);

    /// <summary>ffmpeg executable; a bare name is looked up on PATH.</summary>
    public string FfmpegPath { get; set; } = "ffmpeg";

    /// <summary>Second of the video used as poster.</summary>
    public int VideoSeekSeconds { get; set; } = 4;

    /// <summary>Maximum thumbnail width in pixels.</summary>
    public int Width { get; set; } = 480;

    /// <summary>
    /// Images larger than this (in KB) are previewed through their thumbnail, never the original: a library can
    /// hold photos of tens of MB, too heavy to load by the dozen in a listing. Smaller images are shown as they are,
    /// without running ffmpeg. 0 previews every image through its thumbnail.
    /// </summary>
    public int LargeImageKilobytes { get; set; } = 512;

    /// <summary>
    /// The "one per row" view shows images themselves rather than their thumbnails, whatever their size (see
    /// <see cref="LargeImageKilobytes"/>). Off by default: a folder of large photos then downloads every original it
    /// scrolls past.
    /// </summary>
    public bool SingleViewOriginals { get; set; }

    /// <summary>
    /// Resting the mouse on an animated image the listing shows through its thumbnail (see
    /// <see cref="LargeImageKilobytes"/>) plays the original in its place; each hover downloads it whole.
    /// </summary>
    public bool AnimatedHover { get; set; } = true;

    /// <summary>
    /// Library folders (relative to the root, with their sub-folders) whose files get no new thumbnails; ones
    /// already made are still shown.
    /// </summary>
    public List<string> ExcludedFolders { get; set; } = [];

    /// <summary>Thumbnails generated concurrently.</summary>
    public int MaxConcurrency { get; set; } = 2;

    public int TimeoutSeconds { get; set; } = 30;
}
