using Sfe.Core.Access;

namespace Sfe.Core.Settings;

/// <summary>
/// Every setting of <see cref="EditableSettings"/>, with the level it can be set down to and its right.
/// </summary>
public static class SettingCatalog
{
    private static readonly HashSet<string> ServerKeys =
    [
        "dataServer",
        "browse.maxPageSize",
        "search.maxPageSize",
        "scan.parallelism",
        "thumbnails.maxConcurrency",
        "thumbnails.timeoutSeconds",
        "trickplay.parallelism",
        "trickplay.timeoutMinutes",
        "tasks.logRetentionDays",
        "logs.retentionDays",
        "logs.writeFiles",
        "streaming.maxStreams",
        "streaming.quality",
        "streaming.maxHeight",
        "streaming.copyVideo",
        "openBrowser",
        "fastSignIn",
        // Whether the server calls other storage at all: whoever runs it decides.
        "connections.enabled",
        "connections.providers",
        "connections.levels",
        "connections.dropbox.appKey",
    ];

    private static readonly HashSet<string> UserKeys =
    [
        "appearance.theme",
        "browse.defaultPageSize",
        "search.defaultPageSize",
        "browse.folderPictures",
        "browse.showResolution",
        "browse.showDuration",
        "browse.resolutionMark",
        "thumbnails.singleViewOriginals",
        "thumbnails.animatedHover",
        "player.seekSeconds",
        "player.loop",
        "trickplay.hoverPreview",
        "search.historySize",
        "tags.sidebarCount",
        // How text reads in the viewer: a library default, each account's own.
        "reading.theme",
        "reading.customFont",
        "reading.code.font",
        "reading.code.size",
        "reading.code.lineHeight",
        "reading.code.width",
        "reading.code.padding",
        "reading.code.wrap",
        "reading.code.lineNumbers",
        "reading.prose.font",
        "reading.prose.size",
        "reading.prose.lineHeight",
        "reading.prose.width",
        "reading.prose.padding",
        "reading.prose.wrap",
        "reading.prose.lineNumbers",
    ];

    private static readonly HashSet<string> PerLibraryKeys =
    [
        "scan.excludedFolders",
        "trickplay.folders",
        "trickplay.excludedFolders",
        "thumbnails.excludedFolders",
        "editor.outputFolder",
        "keepAlive.folders",
    ];

    /// <summary>The right of a key, by its first part (the settings page's sections).</summary>
    private static readonly (string Prefix, string Right)[] RightsByPrefix =
    [
        ("appearance.", Rights.InterfaceSettings),
        ("reading.", Rights.InterfaceSettings),
        ("browse.", Rights.InterfaceSettings),
        ("search.", Rights.InterfaceSettings),
        ("player.", Rights.PlaybackSettings),
        ("dataServer", Rights.PlaybackSettings),
        ("streaming.", Rights.PlaybackSettings),
        ("showHiddenItems", Rights.ScanningSettings),
        ("scan.", Rights.ScanningSettings),
        ("fileTypes.", Rights.ScanningSettings),
        ("thumbnails.", Rights.ThumbnailSettings),
        ("trickplay.", Rights.TrickplaySettings),
        ("fileTracking.", Rights.TrackingSettings),
        ("tags.", Rights.TagSettings),
        ("tasks.", Rights.TaskSettings),
        ("logs.", Rights.LogSettings),
        ("editor.", Rights.EditorSettings),
        ("files.", Rights.FileSettings),
        ("keepAlive.", Rights.KeepAliveSettings),
        // How the server starts: only its administrators (System › Start-up).
        ("openBrowser", Rights.ServerSettings),
        ("fastSignIn", Rights.ServerSettings),
        ("connections.", Rights.ServerSettings),
    ];

    public static IReadOnlyList<SettingDefinition> All { get; } =
        SettingKeys
            .Flatten(SettingKeys.ToJson(new EditableSettings()))
            .Keys.Select(key => new SettingDefinition(
                key,
                ServerKeys.Contains(key) ? SettingLevel.Server
                    : UserKeys.Contains(key) ? SettingLevel.User
                    : SettingLevel.Tenant,
                RightsByPrefix.First(p => key.StartsWith(p.Prefix, StringComparison.Ordinal)).Right,
                PerLibraryKeys.Contains(key)
            ))
            .ToList();

    private static readonly Dictionary<string, SettingDefinition> ByKey = All.ToDictionary(
        d => d.Key,
        StringComparer.Ordinal
    );

    public static SettingDefinition? Find(string key) => ByKey.GetValueOrDefault(key);

    /// <summary>
    /// <paramref name="pattern"/> names <paramref name="key"/>: equal, or a prefix ending with <c>*</c>
    /// (<c>player.*</c>; <c>*</c> alone names every setting).
    /// </summary>
    public static bool Matches(string pattern, string key) => RightCatalog.MatchesName(pattern, key);
}
