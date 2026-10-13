using System.Text.Json.Serialization;
using System.Text.RegularExpressions;

namespace Sfe.Core.Options;

/// <summary>
/// Application settings, bound from the <c>Sfe</c> configuration section.
/// Saved values from the web client's Settings page override these (<see cref="SettingsFile"/>).
/// </summary>
public sealed class SfeOptions
{
    public const string SectionName = "Sfe";

    /// <summary>
    /// Library root. Every path exposed by the API is relative to it (e.g. <c>V:\</c> or <c>/srv/media</c>).
    /// A leading <c>~</c> stands for the user's home folder.
    /// </summary>
    public string Root { get; set; } = string.Empty;

    /// <summary>
    /// Optional external static server that already publishes <see cref="Root"/>: its base address (the file's
    /// path is appended) or a URL template with <c>{url}</c> and/or <c>{encodedUrl}</c> (<see cref="DataServerUrl"/>).
    /// When empty, files are streamed by the API itself through <c>/api/files/raw/{path}</c>.
    /// </summary>
    public string? DataServer { get; set; }

    /// <summary>Show files and folders flagged as hidden or system (on Linux and macOS: names starting with a dot).</summary>
    public bool ShowHiddenItems { get; set; }

    public DatabaseOptions Database { get; set; } = new();
    public BrowseOptions Browse { get; set; } = new();
    public SearchOptions Search { get; set; } = new();
    public ScanOptions Scan { get; set; } = new();
    public FileTypeOptions FileTypes { get; set; } = new();
    public ThumbnailOptions Thumbnails { get; set; } = new();
    public TrickplayOptions Trickplay { get; set; } = new();
    public SystemCommandOptions System { get; set; } = new();
    public TagOptions Tags { get; set; } = new();
    public PlayerOptions Player { get; set; } = new();
    public FileTrackingOptions FileTracking { get; set; } = new();
    public AppearanceOptions Appearance { get; set; } = new();
    public TaskOptions Tasks { get; set; } = new();
    public EditorOptions Editor { get; set; } = new();
    public SetupOptions Setup { get; set; } = new();
    public SecurityOptions Security { get; set; } = new();
    public RightOptions Rights { get; set; } = new();
    public TenantOptions Tenants { get; set; } = new();

    /// <summary>
    /// Settings the web client cannot change at any level: the configured value applies to every library and user.
    /// Keys as the settings page names them (<c>player.seekSeconds</c>), <c>*</c> ending a prefix (<c>player.*</c>).
    /// </summary>
    public List<string> Locked { get; set; } = [];

    /// <summary>
    /// File holding the settings changed from the web client's Settings page; they take precedence over this
    /// configuration. Relative paths are resolved against the application content root.
    /// </summary>
    public string SettingsFile { get; set; } = "data/settings.json";
}

public sealed class TagOptions
{
    /// <summary>Record the tags that are searched for and the tags of viewed files (the sidebar's history lists).</summary>
    public bool RecordHistory { get; set; } = true;

    /// <summary>Tags shown in each section of the client's sidebar.</summary>
    public int SidebarCount { get; set; } = 5;
}

/// <summary>The web client's video player.</summary>
public sealed partial class PlayerOptions
{
    /// <summary>Seconds jumped by the back/forward buttons, the J/L keys and a double tap on a side of the picture.</summary>
    public int SeekSeconds { get; set; } = 10;

    /// <summary>Videos start over at their end; the player's loop button switches it for the video on screen.</summary>
    public bool Loop { get; set; }

    /// <summary>Name of the external player on its button (e.g. "Open in VLC").</summary>
    public string? ExternalName { get; set; } = "VLC";

    /// <summary>
    /// Link opening a file in an external player, handled by the app that registered its scheme (<c>vlc://</c>,
    /// <c>potplayer://</c>, <c>infuse://</c>, an Android <c>intent:</c>…). <c>{url}</c> stands for the file's full
    /// address, <c>{encodedUrl}</c> for the same address percent-encoded (for a query string); without either the
    /// address is appended. Empty = no external player button.
    /// </summary>
    public string? ExternalUrl { get; set; } = "vlc://{url}";

    /// <summary>
    /// Minutes a link given to an external player works. The player fetches the file itself, without the browser's
    /// sign-in, so the link carries a token of its own; it must outlast a whole film, as players ask again on each seek.
    /// </summary>
    public int LinkLifetimeMinutes { get; set; } = 360;

    /// <summary>
    /// The link starts with a scheme other than those running script or showing content in the page itself: a library's
    /// administrator sets it for everyone, so a <c>javascript:</c> link would run in the browser of every user and guest.
    /// </summary>
    public static bool IsAllowedExternalUrl(string url)
    {
        var scheme = SchemeRegex().Match(url);
        return scheme.Success && !BlockedSchemes.Contains(scheme.Groups[1].Value);
    }

    private static readonly HashSet<string> BlockedSchemes = new(["javascript", "vbscript", "data", "blob", "file", "about"], StringComparer.OrdinalIgnoreCase);

    [GeneratedRegex(@"^([a-zA-Z][a-zA-Z0-9+.-]*):")]
    private static partial Regex SchemeRegex();
}

/// <summary>
/// Colors of the web client. Written as a name in the settings file too (it is serialized without the API's enum
/// converter), so that the file stays readable and editable by hand.
/// </summary>
[JsonConverter(typeof(JsonStringEnumConverter<Theme>))]
public enum Theme
{
    /// <summary>Follows the light or dark mode of the device.</summary>
    System,
    Dark,
    Light,
}

/// <summary>The web client's look.</summary>
public sealed class AppearanceOptions
{
    /// <summary>
    /// Theme of the browsers that have not picked one in Administration › Interface › Appearance (that choice is kept per browser and wins).
    /// </summary>
    public Theme Theme { get; set; } = Theme.System;
}

/// <summary>The background tasks (scans, trickplay, similar videos): their run history.</summary>
public sealed class TaskOptions
{
    /// <summary>Days the runs of the background tasks are kept, with their logs; 0 = forever.</summary>
    public int LogRetentionDays { get; set; } = 30;
}

/// <summary>
/// The web client's picture editor (crop...). The browser applies the changes and sends the result, which the server
/// saves as a new file: the original is never changed.
/// </summary>
public sealed class EditorOptions
{
    /// <summary>
    /// Library folder (relative to the root, separated by <c>/</c>) where edited pictures are saved, in the same
    /// sub-folders as their originals: <c>Photos/2024/beach.jpg</c> is saved as <c>Edited/Photos/2024/beach.jpg</c>.
    /// Empty = next to the original, as <c>beach (edited).jpg</c>. Created when first needed.
    /// </summary>
    public string? OutputFolder { get; set; } = "Edited";

    /// <summary>Quality (1-100) of the JPEG and WebP pictures the editor saves; PNG pictures are lossless.</summary>
    public int Quality { get; set; } = 92;
}

/// <summary>Duplicate files and files moved, renamed or deleted since they were indexed.</summary>
public sealed class FileTrackingOptions
{
    public DuplicateOptions Duplicates { get; set; } = new();
    public RelinkOptions Relink { get; set; } = new();
    public SimilarVideoOptions SimilarVideos { get; set; } = new();
}

/// <summary>
/// Duplicates are indexed files of the same size and fingerprint (<see cref="ScanOptions.ComputeHashes"/>): files
/// indexed without a fingerprint are never listed.
/// </summary>
public sealed class DuplicateOptions
{
    /// <summary>Smaller files are left out: a library is full of identical tiny files (empty files, sidecars).</summary>
    public int MinimumKilobytes { get; set; } = 1;

    /// <summary>Groups of identical files loaded at a time.</summary>
    public int GroupsPerPage { get; set; } = 50;

    /// <summary>
    /// Offer to delete a copy from the disk, its tags going to the copy kept. The two files are compared byte for
    /// byte first, so the content is never lost.
    /// </summary>
    public bool AllowRemove { get; set; } = true;
}

/// <summary>
/// A tagged file that a scan no longer finds is remembered as missing, with its tags, instead of being forgotten.
/// When a scan then indexes the same file somewhere else (moved, renamed, or back after a drive was missing), its
/// tags move over to it.
/// </summary>
public sealed class RelinkOptions
{
    /// <summary>When off, files no longer found are removed from the index with their tags.</summary>
    public bool Enabled { get; set; } = true;

    /// <summary>
    /// Files without a fingerprint are matched on their name, size and modification time instead. When off, only
    /// fingerprinted files are relinked.
    /// </summary>
    public bool MatchByName { get; set; } = true;

    /// <summary>Days a missing file is remembered; 0 = until it is found or forgotten by hand.</summary>
    public int RetentionDays { get; set; } = 30;
}

/// <summary>
/// Videos with the same pictures but other bytes (another resolution, bitrate or codec, trimmed), found from the
/// fingerprints made with their trickplay (<see cref="TrickplayOptions"/>): videos without trickplay are not compared.
/// </summary>
public sealed class SimilarVideoOptions
{
    /// <summary>
    /// Share of matching frames (out of the longer video) from which two videos are listed as similar. Applies from
    /// the next comparison.
    /// </summary>
    public int MinimumPercent { get; set; } = 80;
}

public enum DatabaseProvider
{
    SqlServer,
    Sqlite,
}

public sealed class DatabaseOptions
{
    public DatabaseProvider Provider { get; set; } = DatabaseProvider.SqlServer;

    /// <summary>Apply migrations (SQL Server) or create the schema (SQLite) on startup.</summary>
    public bool MigrateOnStartup { get; set; } = true;
}

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
    /// The web client marks the previews of images and videos whose resolution the index knows as high (HR) or low
    /// (LR) resolution, by <see cref="ImageHighResolutionPixels"/> and <see cref="VideoHighResolutionPixels"/>. Off by
    /// default: one more label on every picture. The search terms <c>r:hr</c> and <c>r:lr</c> use the thresholds either way.
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

public sealed class SearchOptions
{
    public int DefaultPageSize { get; set; } = 120;
    public int MaxPageSize { get; set; } = 1000;

    /// <summary>
    /// The web client offers to put the typed query into words (button in the search box). Only a client feature:
    /// off hides the button for everyone using the library.
    /// </summary>
    public bool Explain { get; set; } = true;

    /// <summary>
    /// Searches each browser remembers and suggests again as the box is typed in, newest first; 0 = none (nothing
    /// is suggested or recorded). Kept by each browser (visitors without an account have no other place), so the server
    /// only sends the number.
    /// </summary>
    public int HistorySize { get; set; } = 10;
}

public sealed class ScanOptions
{
    /// <summary>Folder names skipped by the indexer.</summary>
    public List<string> IgnoredDirectories { get; set; } = [];

    /// <summary>File names skipped by the indexer.</summary>
    public List<string> IgnoredFiles { get; set; } = [];

    /// <summary>
    /// Library folders (relative to the root, with their sub-folders) that scans skip: removed from the index by
    /// the next scan of their parent, and never indexed again until taken off the list.
    /// </summary>
    public List<string> ExcludedFolders { get; set; } = [];

    /// <summary>Directories indexed concurrently.</summary>
    public int Parallelism { get; set; } = 4;

    /// <summary>Compute the partial SHA1 fingerprint of each file (reads up to 1 MB per new/changed file).</summary>
    public bool ComputeHashes { get; set; } = true;

    /// <summary>
    /// Read the resolution of each new or changed image and video, and the duration of videos and audio files, for the
    /// listings and the <c>w:</c>, <c>h:</c>, <c>r:</c> and <c>d:</c> search terms. Images are read from their first
    /// bytes; videos and audio need ffmpeg, which makes scans of new ones slower. Files indexed before it was turned on
    /// (and videos and audio indexed before durations were read) are read by the next scan.
    /// </summary>
    public bool ReadResolution { get; set; } = true;
}

/// <remarks>
/// The lists start empty and get their defaults from <see cref="ApplyDefaults"/> when not configured: the
/// configuration binder adds configured items to a list's initial items instead of replacing them.
/// </remarks>
public sealed class FileTypeOptions
{
    public static readonly string[] DefaultImages = ["bmp", "gif", "jpeg", "jpg", "png", "tif", "tiff", "webp", "avif"];
    public static readonly string[] DefaultVideos = ["3gp", "avi", "flv", "m4v", "mkv", "mov", "mp4", "mpeg", "mpg", "ts", "webm", "wmv"];
    public static readonly string[] DefaultAudio = ["aac", "flac", "m4a", "mp3", "ogg", "opus", "wav"];

    public List<string> Images { get; set; } = [];
    public List<string> Videos { get; set; } = [];
    public List<string> Audio { get; set; } = [];

    /// <param name="isConfigured">Whether the configuration has a value for the named list.</param>
    public void ApplyDefaults(Func<string, bool> isConfigured)
    {
        if (!isConfigured(nameof(Images)))
            Images = [.. DefaultImages];
        if (!isConfigured(nameof(Videos)))
            Videos = [.. DefaultVideos];
        if (!isConfigured(nameof(Audio)))
            Audio = [.. DefaultAudio];
    }
}

public sealed class ThumbnailOptions
{
    public bool Enabled { get; set; } = true;

    /// <summary>
    /// Disk cache folder.
    /// Relative paths are resolved against the application content root; a leading <c>~</c> is the home folder.
    /// </summary>
    public string CacheDirectory { get; set; } = "data/thumbs";

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

    /// <summary>Thumbnails generated concurrently.</summary>
    public int MaxConcurrency { get; set; } = 2;

    public int TimeoutSeconds { get; set; } = 30;
}

/// <summary>
/// Trickplay: frames of a video, taken at regular intervals and tiled on sheets, that the player shows over its seek
/// bar. Rendered with ffmpeg by a background job and kept under <c>{Thumbnails:CacheDirectory}/trickplay</c>.
/// </summary>
public sealed class TrickplayOptions
{
    /// <summary>Off by default: a first pass over a large library keeps ffmpeg busy for a long time.</summary>
    public bool Enabled { get; set; }

    /// <summary>Library folders (relative to the root, with their sub-folders) whose videos get trickplay; empty = the whole library.</summary>
    public List<string> Folders { get; set; } = [];

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

    /// <summary>Time limit for one video; a video taking longer is given up on until it or the settings change.</summary>
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

public sealed class SystemCommandOptions
{
    /// <summary>Expose <c>POST /api/system/shutdown</c>.</summary>
    public bool EnableShutdown { get; set; }

    /// <summary>
    /// Shell command run by <c>POST /api/system/shutdown</c>. When empty: <c>shutdown /s /f /t 5</c> on Windows,
    /// <c>shutdown -h +1</c> elsewhere (the service account needs the right to shut down, e.g. through sudo or polkit).
    /// </summary>
    public string? ShutdownCommand { get; set; }

    /// <summary>
    /// Shell command run by <c>POST /api/system/dismount</c> (<c>cmd.exe /c</c> on Windows, <c>/bin/sh -c</c>
    /// elsewhere); disabled when empty.
    /// </summary>
    public string? DismountCommand { get; set; }
}
