using Sfe.Core.Options;

namespace Sfe.Core.Settings;

/// <summary>
/// The part of <see cref="SfeOptions"/> that can be changed from the web client, by level
/// (<see cref="SettingCatalog"/>). Left out on purpose: the library root, the thumbnail cache folder, the database, the
/// ffmpeg executable and the system commands. Those run programs or decide where data lives: only whoever runs the
/// server sets them, in the configuration files.
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
    public ConnectionSettings Connections { get; set; } = new();
    public SecuritySettings Security { get; set; } = new();

    public static EditableSettings From(SfeOptions options) =>
        new()
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
                Flash = options.Player.Flash,
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
                Uploads = new()
                {
                    MaxFileSizeMegabytes = options.Files.Uploads.MaxFileSizeMegabytes,
                    KeepUnfinishedHours = options.Files.Uploads.KeepUnfinishedHours,
                },
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
            Connections = ConnectionSettings.From(options.Connections),
            Security = SecuritySettings.From(options.Security),
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
        options.Player.Flash = Player.Flash;
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
        Connections.ApplyTo(options.Connections);
        Security.ApplyTo(options.Security);
    }
}
