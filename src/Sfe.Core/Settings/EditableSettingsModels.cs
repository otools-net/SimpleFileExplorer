using Otools.Net.Sfe.Core.Options;

namespace Otools.Net.Sfe.Core.Settings;

public class PageSettings
{
    public int DefaultPageSize { get; set; }
    public int MaxPageSize { get; set; }
}

public sealed class BrowseSettings : PageSettings
{
    public int FolderPictures { get; set; }
    public bool CopyPaths { get; set; }
    public bool LiveChanges { get; set; }
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
    public bool IndexWhenListed { get; set; }
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
    public bool Flash { get; set; }
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
/// <param name="Customized">The level has values of its own (for the server: the settings file), rather than all
/// inherited.</param>
/// <param name="Level">The level the values are read at.</param>
/// <param name="Keys">For each setting, where its value comes from and whether the level may change or lock it; null
/// without levels.</param>
public sealed record SettingsView(
    EditableSettings Values,
    bool Customized,
    SettingLevel Level = SettingLevel.Server,
    IReadOnlyDictionary<string, SettingState>? Keys = null
);

public sealed class TaskSettings
{
    public int LogRetentionDays { get; set; }
}

/// <summary>
/// The server's log; its folder (<see cref="LogOptions.Directory"/>) is only set in the configuration.
/// </summary>
public sealed class LogSettings
{
    public int RetentionDays { get; set; }
    public bool WriteFiles { get; set; }
}

public sealed class EditorSettings
{
    /// <summary>
    /// A library folder, relative to the root and separated by <c>/</c>; empty = next to the originals.
    /// </summary>
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
