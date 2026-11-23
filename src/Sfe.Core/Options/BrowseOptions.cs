namespace Otools.Net.Sfe.Core.Options;

public sealed class BrowseOptions
{
    public int DefaultPageSize { get; set; } = 120;
    public int MaxPageSize { get; set; } = 1000;

    /// <summary>
    /// Pictures shown on a folder's card in the grid and "one per row" views (2×2 for 4, 3×3 for 9...);
    /// 0 lists folders without pictures. At most <see cref="Library.LibraryBrowser.MaxFolderPictures"/>.
    /// </summary>
    public int FolderPictures { get; set; } = 4;

    /// <summary>
    /// The web client offers buttons copying the full path (on the server) of the folder browsed and of its files
    /// and folders. Off by default: it tells every browser where the library is on the server's disk.
    /// </summary>
    public bool CopyPaths { get; set; }

    /// <summary>
    /// The folder listed is listed again when it changes outside SFE (another program, the share's other users), not
    /// only through SFE: the server watches the folders open in the web client (agents/docs/features/change-feed.md).
    /// A user setting; off, the client neither asks for it nor heeds such changes.
    /// </summary>
    public bool LiveChanges { get; set; } = true;

    /// <summary>
    /// The web client shows the resolution of images and videos (e.g. 1920×1080) next to their size, when the index
    /// has it (<see cref="ScanOptions.ReadResolution"/>).
    /// </summary>
    public bool ShowResolution { get; set; } = true;

    /// <summary>
    /// The web client shows the duration of videos and audio files (e.g. 1:23:45) next to their size, when the
    /// index has it (read with the resolution, <see cref="ScanOptions.ReadResolution"/>).
    /// </summary>
    public bool ShowDuration { get; set; } = true;

    /// <summary>
    /// The web client marks the previews of images and videos whose resolution the index knows as high (HR) or low (LR)
    /// resolution, by <see cref="ImageHighResolutionPixels"/> and <see cref="VideoHighResolutionPixels"/>. Off by
    /// default: one more label on every picture. The search terms <c>r:hr</c> and <c>r:lr</c> use the thresholds either
    /// way.
    /// </summary>
    public bool ResolutionMark { get; set; }

    /// <summary>
    /// Smallest shorter side, in pixels, of a high-resolution image: 1500 makes a 2000×1500 photo HR, a 1000×1000 one
    /// LR. The shorter side, so that the orientation does not matter. Separate from videos, whose pictures are smaller.
    /// </summary>
    public int ImageHighResolutionPixels { get; set; } = 1500;

    /// <summary>
    /// Smallest shorter side, in pixels, of a high-resolution video: 720 makes 1280×720 (and a portrait 720×1280) HR,
    /// 854×480 LR.
    /// </summary>
    public int VideoHighResolutionPixels { get; set; } = 720;
}
