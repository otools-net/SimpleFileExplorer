using Sfe.Core.Options;

namespace Sfe.Core.Settings;

/// <summary>
/// The part of <see cref="SfeOptions"/> that can be changed from the web client, by level (<see cref="SettingCatalog"/>).
/// Left out on purpose: the library root, the thumbnail cache folder, the database, the ffmpeg executable and the system
/// commands. Those run programs or decide where data lives: only whoever runs the server sets them, in the
/// configuration files.
/// </summary>
public sealed class EditableSettings
{
    /// <inheritdoc cref="SfeOptions.DataServer"/>
    public string? DataServer { get; set; }

    /// <inheritdoc cref="SfeOptions.ShowHiddenItems"/>
    public bool ShowHiddenItems { get; set; }

    /// <inheritdoc cref="SfeOptions.OpenBrowser"/>
    public bool OpenBrowser { get; set; } = true;

    /// <inheritdoc cref="SfeOptions.FastSignIn"/>
    public bool FastSignIn { get; set; }

    public BrowseSettings Browse { get; set; } = new();
    public SearchSettings Search { get; set; } = new();
    public ScanSettings Scan { get; set; } = new();
    public FileTypeSettings FileTypes { get; set; } = new();
    public ThumbnailSettings Thumbnails { get; set; } = new();
    public TrickplaySettings Trickplay { get; set; } = new();
    public TagSettings Tags { get; set; } = new();
    public PlayerSettings Player { get; set; } = new();
    public StreamingSettings Streaming { get; set; } = new();
    public FileTrackingSettings FileTracking { get; set; } = new();
    public AppearanceSettings Appearance { get; set; } = new();
    public ReadingOptions Reading { get; set; } = new();
    public TaskSettings Tasks { get; set; } = new();
    public LogSettings Logs { get; set; } = new();
    public EditorSettings Editor { get; set; } = new();
    public FileSettings Files { get; set; } = new();
    public KeepAliveSettings KeepAlive { get; set; } = new();

    public static EditableSettings From(SfeOptions options) => new()
    {
        DataServer = options.DataServer,
        ShowHiddenItems = options.ShowHiddenItems,
        OpenBrowser = options.OpenBrowser,
        FastSignIn = options.FastSignIn,
        Browse = new()
        {
            DefaultPageSize = options.Browse.DefaultPageSize,
            MaxPageSize = options.Browse.MaxPageSize,
            FolderPictures = options.Browse.FolderPictures,
            CopyPaths = options.Browse.CopyPaths,
            ShowResolution = options.Browse.ShowResolution,
            ShowDuration = options.Browse.ShowDuration,
            ResolutionMark = options.Browse.ResolutionMark,
            ImageHighResolutionPixels = options.Browse.ImageHighResolutionPixels,
            VideoHighResolutionPixels = options.Browse.VideoHighResolutionPixels,
        },
        Search = new()
        {
            DefaultPageSize = options.Search.DefaultPageSize,
            MaxPageSize = options.Search.MaxPageSize,
            Explain = options.Search.Explain,
            HistorySize = options.Search.HistorySize,
        },
        Scan = new()
        {
            IgnoredDirectories = [.. options.Scan.IgnoredDirectories],
            IgnoredFiles = [.. options.Scan.IgnoredFiles],
            ExcludedFolders = [.. options.Scan.ExcludedFolders],
            Parallelism = options.Scan.Parallelism,
            ComputeHashes = options.Scan.ComputeHashes,
            ReadResolution = options.Scan.ReadResolution,
        },
        FileTypes = new()
        {
            Images = [.. options.FileTypes.Images],
            Videos = [.. options.FileTypes.Videos],
            Audio = [.. options.FileTypes.Audio],
            Text = [.. options.FileTypes.Text],
        },
        Thumbnails = new()
        {
            Enabled = options.Thumbnails.Enabled,
            VideoSeekSeconds = options.Thumbnails.VideoSeekSeconds,
            Width = options.Thumbnails.Width,
            LargeImageKilobytes = options.Thumbnails.LargeImageKilobytes,
            SingleViewOriginals = options.Thumbnails.SingleViewOriginals,
            AnimatedHover = options.Thumbnails.AnimatedHover,
            ExcludedFolders = [.. options.Thumbnails.ExcludedFolders],
            MaxConcurrency = options.Thumbnails.MaxConcurrency,
            TimeoutSeconds = options.Thumbnails.TimeoutSeconds,
        },
        Trickplay = new()
        {
            Enabled = options.Trickplay.Enabled,
            Folders = [.. options.Trickplay.Folders],
            ExcludedFolders = [.. options.Trickplay.ExcludedFolders],
            IntervalSeconds = options.Trickplay.IntervalSeconds,
            Width = options.Trickplay.Width,
            Columns = options.Trickplay.Columns,
            Rows = options.Trickplay.Rows,
            KeyFramesOnly = options.Trickplay.KeyFramesOnly,
            Parallelism = options.Trickplay.Parallelism,
            TimeoutMinutes = options.Trickplay.TimeoutMinutes,
            RecheckHours = options.Trickplay.RecheckHours,
            HoverPreview = options.Trickplay.HoverPreview,
        },
        Tags = new() { RecordHistory = options.Tags.RecordHistory, SidebarCount = options.Tags.SidebarCount },
        Player = new()
        {
            SeekSeconds = options.Player.SeekSeconds,
            Loop = options.Player.Loop,
            ExternalName = options.Player.ExternalName,
            ExternalUrl = options.Player.ExternalUrl,
            LinkLifetimeMinutes = options.Player.LinkLifetimeMinutes,
        },
        Streaming = new()
        {
            Mode = options.Streaming.Mode,
            MaxStreams = options.Streaming.MaxStreams,
            Quality = options.Streaming.Quality,
            MaxHeight = options.Streaming.MaxHeight,
            CopyVideo = options.Streaming.CopyVideo,
        },
        FileTracking = new()
        {
            Duplicates = new()
            {
                MinimumKilobytes = options.FileTracking.Duplicates.MinimumKilobytes,
                GroupsPerPage = options.FileTracking.Duplicates.GroupsPerPage,
                AllowRemove = options.FileTracking.Duplicates.AllowRemove,
            },
            Relink = new()
            {
                Enabled = options.FileTracking.Relink.Enabled,
                MatchByName = options.FileTracking.Relink.MatchByName,
                RetentionDays = options.FileTracking.Relink.RetentionDays,
            },
            SimilarVideos = new() { MinimumPercent = options.FileTracking.SimilarVideos.MinimumPercent },
        },
        Appearance = new() { Theme = options.Appearance.Theme },
        Reading = options.Reading.Copy(),
        Tasks = new() { LogRetentionDays = options.Tasks.LogRetentionDays },
        Logs = new() { RetentionDays = options.Logs.RetentionDays, WriteFiles = options.Logs.WriteFiles },
        Editor = new() { OutputFolder = options.Editor.OutputFolder, Quality = options.Editor.Quality },
        Files = new()
        {
            PortableNames = options.Files.PortableNames,
            WorkFolder = options.Files.WorkFolder,
            DeletePermanently = options.Files.DeletePermanently,
            Trash = new() { RetentionDays = options.Files.Trash.RetentionDays },
            MinimumFreeSpaceMegabytes = options.Files.MinimumFreeSpaceMegabytes,
            Uploads = new() { MaxFileSizeMegabytes = options.Files.Uploads.MaxFileSizeMegabytes, KeepUnfinishedHours = options.Files.Uploads.KeepUnfinishedHours },
            MaxArchiveMegabytes = options.Files.MaxArchiveMegabytes,
        },
        KeepAlive = new()
        {
            Mode = options.KeepAlive.Mode,
            IntervalSeconds = options.KeepAlive.IntervalSeconds,
            TimeoutSeconds = options.KeepAlive.TimeoutSeconds,
            Folders = [.. options.KeepAlive.Folders],
            Method = options.KeepAlive.Method,
        },
    };

    public void ApplyTo(SfeOptions options)
    {
        options.DataServer = DataServer;
        options.ShowHiddenItems = ShowHiddenItems;
        options.OpenBrowser = OpenBrowser;
        options.FastSignIn = FastSignIn;
        options.Browse.DefaultPageSize = Browse.DefaultPageSize;
        options.Browse.MaxPageSize = Browse.MaxPageSize;
        options.Browse.FolderPictures = Browse.FolderPictures;
        options.Browse.CopyPaths = Browse.CopyPaths;
        options.Browse.ShowResolution = Browse.ShowResolution;
        options.Browse.ShowDuration = Browse.ShowDuration;
        options.Browse.ResolutionMark = Browse.ResolutionMark;
        options.Browse.ImageHighResolutionPixels = Browse.ImageHighResolutionPixels;
        options.Browse.VideoHighResolutionPixels = Browse.VideoHighResolutionPixels;
        options.Search.DefaultPageSize = Search.DefaultPageSize;
        options.Search.MaxPageSize = Search.MaxPageSize;
        options.Search.Explain = Search.Explain;
        options.Search.HistorySize = Search.HistorySize;
        options.Scan.IgnoredDirectories = [.. Scan.IgnoredDirectories];
        options.Scan.IgnoredFiles = [.. Scan.IgnoredFiles];
        options.Scan.ExcludedFolders = [.. Scan.ExcludedFolders];
        options.Scan.Parallelism = Scan.Parallelism;
        options.Scan.ComputeHashes = Scan.ComputeHashes;
        options.Scan.ReadResolution = Scan.ReadResolution;
        options.FileTypes.Images = [.. FileTypes.Images];
        options.FileTypes.Videos = [.. FileTypes.Videos];
        options.FileTypes.Audio = [.. FileTypes.Audio];
        options.FileTypes.Text = [.. FileTypes.Text];
        options.Thumbnails.Enabled = Thumbnails.Enabled;
        options.Thumbnails.VideoSeekSeconds = Thumbnails.VideoSeekSeconds;
        options.Thumbnails.Width = Thumbnails.Width;
        options.Thumbnails.LargeImageKilobytes = Thumbnails.LargeImageKilobytes;
        options.Thumbnails.SingleViewOriginals = Thumbnails.SingleViewOriginals;
        options.Thumbnails.AnimatedHover = Thumbnails.AnimatedHover;
        options.Thumbnails.ExcludedFolders = [.. Thumbnails.ExcludedFolders];
        options.Thumbnails.MaxConcurrency = Thumbnails.MaxConcurrency;
        options.Thumbnails.TimeoutSeconds = Thumbnails.TimeoutSeconds;
        options.Trickplay.Enabled = Trickplay.Enabled;
        options.Trickplay.Folders = [.. Trickplay.Folders];
        options.Trickplay.ExcludedFolders = [.. Trickplay.ExcludedFolders];
        options.Trickplay.IntervalSeconds = Trickplay.IntervalSeconds;
        options.Trickplay.Width = Trickplay.Width;
        options.Trickplay.Columns = Trickplay.Columns;
        options.Trickplay.Rows = Trickplay.Rows;
        options.Trickplay.KeyFramesOnly = Trickplay.KeyFramesOnly;
        options.Trickplay.Parallelism = Trickplay.Parallelism;
        options.Trickplay.TimeoutMinutes = Trickplay.TimeoutMinutes;
        options.Trickplay.RecheckHours = Trickplay.RecheckHours;
        options.Trickplay.HoverPreview = Trickplay.HoverPreview;
        options.Tags.RecordHistory = Tags.RecordHistory;
        options.Tags.SidebarCount = Tags.SidebarCount;
        options.Player.SeekSeconds = Player.SeekSeconds;
        options.Player.Loop = Player.Loop;
        options.Player.ExternalName = Player.ExternalName;
        options.Player.ExternalUrl = Player.ExternalUrl;
        options.Player.LinkLifetimeMinutes = Player.LinkLifetimeMinutes;
        options.Streaming.Mode = Streaming.Mode;
        options.Streaming.MaxStreams = Streaming.MaxStreams;
        options.Streaming.Quality = Streaming.Quality;
        options.Streaming.MaxHeight = Streaming.MaxHeight;
        options.Streaming.CopyVideo = Streaming.CopyVideo;
        options.FileTracking.Duplicates.MinimumKilobytes = FileTracking.Duplicates.MinimumKilobytes;
        options.FileTracking.Duplicates.GroupsPerPage = FileTracking.Duplicates.GroupsPerPage;
        options.FileTracking.Duplicates.AllowRemove = FileTracking.Duplicates.AllowRemove;
        options.FileTracking.Relink.Enabled = FileTracking.Relink.Enabled;
        options.FileTracking.Relink.MatchByName = FileTracking.Relink.MatchByName;
        options.FileTracking.Relink.RetentionDays = FileTracking.Relink.RetentionDays;
        options.FileTracking.SimilarVideos.MinimumPercent = FileTracking.SimilarVideos.MinimumPercent;
        options.Appearance.Theme = Appearance.Theme;
        options.Reading = Reading.Copy();
        options.Tasks.LogRetentionDays = Tasks.LogRetentionDays;
        options.Logs.RetentionDays = Logs.RetentionDays;
        options.Logs.WriteFiles = Logs.WriteFiles;
        options.Editor.OutputFolder = Editor.OutputFolder;
        options.Editor.Quality = Editor.Quality;
        options.Files.PortableNames = Files.PortableNames;
        options.Files.WorkFolder = Files.WorkFolder;
        options.Files.DeletePermanently = Files.DeletePermanently;
        options.Files.Trash.RetentionDays = Files.Trash.RetentionDays;
        options.Files.MinimumFreeSpaceMegabytes = Files.MinimumFreeSpaceMegabytes;
        options.Files.Uploads.MaxFileSizeMegabytes = Files.Uploads.MaxFileSizeMegabytes;
        options.Files.Uploads.KeepUnfinishedHours = Files.Uploads.KeepUnfinishedHours;
        options.Files.MaxArchiveMegabytes = Files.MaxArchiveMegabytes;
        options.KeepAlive.Mode = KeepAlive.Mode;
        options.KeepAlive.IntervalSeconds = KeepAlive.IntervalSeconds;
        options.KeepAlive.TimeoutSeconds = KeepAlive.TimeoutSeconds;
        options.KeepAlive.Folders = [.. KeepAlive.Folders];
        options.KeepAlive.Method = KeepAlive.Method;
    }
}

public class PageSettings
{
    public int DefaultPageSize { get; set; }
    public int MaxPageSize { get; set; }
}

public sealed class BrowseSettings : PageSettings
{
    public int FolderPictures { get; set; }
    public bool CopyPaths { get; set; }
    public bool ShowResolution { get; set; }
    public bool ShowDuration { get; set; }
    public bool ResolutionMark { get; set; }
    public int ImageHighResolutionPixels { get; set; }
    public int VideoHighResolutionPixels { get; set; }
}

public sealed class SearchSettings : PageSettings
{
    public bool Explain { get; set; }
    public int HistorySize { get; set; }
}

public sealed class ScanSettings
{
    public List<string> IgnoredDirectories { get; set; } = [];
    public List<string> IgnoredFiles { get; set; } = [];

    /// <summary>Library folders, relative to the root and separated by <c>/</c>.</summary>
    public List<string> ExcludedFolders { get; set; } = [];
    public int Parallelism { get; set; }
    public bool ComputeHashes { get; set; }
    public bool ReadResolution { get; set; }
}

public sealed class FileTypeSettings
{
    public List<string> Images { get; set; } = [];
    public List<string> Videos { get; set; } = [];
    public List<string> Audio { get; set; } = [];
    public List<string> Text { get; set; } = [];
}

public sealed class ThumbnailSettings
{
    public bool Enabled { get; set; }
    public int VideoSeekSeconds { get; set; }
    public int Width { get; set; }
    public int LargeImageKilobytes { get; set; }
    public bool SingleViewOriginals { get; set; }
    public bool AnimatedHover { get; set; }

    /// <summary>Library folders, relative to the root and separated by <c>/</c>.</summary>
    public List<string> ExcludedFolders { get; set; } = [];
    public int MaxConcurrency { get; set; }
    public int TimeoutSeconds { get; set; }
}

public sealed class TrickplaySettings
{
    public bool Enabled { get; set; }

    /// <summary>Library folders, relative to the root and separated by <c>/</c>.</summary>
    public List<string> Folders { get; set; } = [];

    /// <summary>Library folders, relative to the root and separated by <c>/</c>.</summary>
    public List<string> ExcludedFolders { get; set; } = [];
    public int IntervalSeconds { get; set; }
    public int Width { get; set; }
    public int Columns { get; set; }
    public int Rows { get; set; }
    public bool KeyFramesOnly { get; set; }
    public int Parallelism { get; set; }
    public int TimeoutMinutes { get; set; }
    public int RecheckHours { get; set; }
    public bool HoverPreview { get; set; }
}

public sealed class TagSettings
{
    public bool RecordHistory { get; set; }
    public int SidebarCount { get; set; }
}

public sealed class PlayerSettings
{
    public int SeekSeconds { get; set; }
    public bool Loop { get; set; }
    public string? ExternalName { get; set; }
    public string? ExternalUrl { get; set; }
    public int LinkLifetimeMinutes { get; set; }
}

public sealed class StreamingSettings
{
    public StreamingMode Mode { get; set; }
    public int MaxStreams { get; set; }
    public int Quality { get; set; }
    public int MaxHeight { get; set; }
    public bool CopyVideo { get; set; }
}

public sealed class AppearanceSettings
{
    public Theme Theme { get; set; }
}

public sealed class FileTrackingSettings
{
    public DuplicateSettings Duplicates { get; set; } = new();
    public RelinkSettings Relink { get; set; } = new();
    public SimilarVideoSettings SimilarVideos { get; set; } = new();
}

public sealed class DuplicateSettings
{
    public int MinimumKilobytes { get; set; }
    public int GroupsPerPage { get; set; }
    public bool AllowRemove { get; set; }
}

public sealed class SimilarVideoSettings
{
    public int MinimumPercent { get; set; }
}

public sealed class RelinkSettings
{
    public bool Enabled { get; set; }
    public bool MatchByName { get; set; }
    public int RetentionDays { get; set; }
}

/// <param name="Values">Settings in effect at <paramref name="Level"/>.</param>
/// <param name="Customized">The level has values of its own (for the server: the settings file), rather than all inherited.</param>
/// <param name="Level">The level the values are read at.</param>
/// <param name="Keys">For each setting, where its value comes from and whether the level may change or lock it; null without levels.</param>
public sealed record SettingsView(EditableSettings Values, bool Customized, SettingLevel Level = SettingLevel.Server, IReadOnlyDictionary<string, SettingState>? Keys = null);

public sealed class TaskSettings
{
    public int LogRetentionDays { get; set; }
}

/// <summary>The server's log; its folder (<see cref="LogOptions.Directory"/>) is only set in the configuration.</summary>
public sealed class LogSettings
{
    public int RetentionDays { get; set; }
    public bool WriteFiles { get; set; }
}

public sealed class EditorSettings
{
    /// <summary>A library folder, relative to the root and separated by <c>/</c>; empty = next to the originals.</summary>
    public string? OutputFolder { get; set; }
    public int Quality { get; set; }
}

public sealed class FileSettings
{
    public bool PortableNames { get; set; }
    public WorkFolderLocation WorkFolder { get; set; }
    public bool DeletePermanently { get; set; }
    public TrashSettings Trash { get; set; } = new();
    public int MinimumFreeSpaceMegabytes { get; set; }
    public UploadSettings Uploads { get; set; } = new();
    public int MaxArchiveMegabytes { get; set; }
}

public sealed class UploadSettings
{
    public int MaxFileSizeMegabytes { get; set; }
    public int KeepUnfinishedHours { get; set; }
}

public sealed class TrashSettings
{
    public int RetentionDays { get; set; }
}

public sealed class KeepAliveSettings
{
    public KeepAliveMode Mode { get; set; }
    public int IntervalSeconds { get; set; }
    public int TimeoutSeconds { get; set; }

    /// <summary>Library folders, relative to the root and separated by <c>/</c>.</summary>
    public List<string> Folders { get; set; } = [];
    public KeepAliveMethod Method { get; set; }
}
