using Otools.Net.Sfe.Core.Data;

namespace Otools.Net.Sfe.Core.Access;

public static class RightCatalog
{
    private static readonly BuiltInGroup[] Everyone =
    [
        BuiltInGroup.Administrators,
        BuiltInGroup.Users,
        BuiltInGroup.Guests,
    ];
    private static readonly BuiltInGroup[] Members = [BuiltInGroup.Administrators, BuiltInGroup.Users];
    private static readonly BuiltInGroup[] Administrators = [BuiltInGroup.Administrators];

    // Kind and AtFolder (agents/docs/features/folder-access.md): a right is Write when any of its routes changes
    // something; it needs Write at a folder only when it changes the library's files on disk. Index-only changes
    // (tags, relinks, removal from the index) need the folder to be readable.
    private const RightKind R = RightKind.Read;
    private const RightKind W = RightKind.Write;
    private const FolderLevel Readable = FolderLevel.Read;
    private const FolderLevel Writable = FolderLevel.Write;

    public static IReadOnlyList<RightDefinition> All { get; } =
    [
        Tenant(
            Rights.Browse,
            true,
            Everyone,
            R,
            Readable,
            "Library",
            "Browse and search",
            "List folders, search the index and see how a scan is going."
        ),
        Tenant(
            Rights.ViewFiles,
            true,
            Everyone,
            R,
            Readable,
            "Library",
            "View and play files",
            "Open pictures, play videos and audio, and see thumbnails and previews along the timeline."
        ),
        Tenant(Rights.Download, true, Members, R, Readable, "Library", "Download files", "Save files to your device."),
        Tenant(
            Rights.ExternalPlayer,
            true,
            Members,
            R,
            Readable,
            "Library",
            "Open in an external player",
            "Get a link that opens a file in an app such as VLC without signing in, for a limited time."
        ),
        Tenant(
            Rights.CopyPaths,
            false,
            Administrators,
            R,
            Readable,
            "Library",
            "Copy full paths",
            "See and copy where files are on the server's disk."
        ),
        Tenant(
            Rights.EditImages,
            false,
            Members,
            W,
            Writable,
            "Library",
            "Edit pictures",
            "Edit pictures and save the result as a new file, or over the original where you may also replace "
                + "files.",
            since: 1
        ),
        Tenant(
            Rights.Stream,
            true,
            Members,
            R,
            Readable,
            "Library",
            "Play videos converted by the server",
            "Play videos the browser can't play as they are: the server converts them with ffmpeg while they play, "
                + "which keeps its processor busy.",
            since: 2
        ),
        // File management: nobody gets these by upgrade or by default.
        Tenant(
            Rights.CreateFolders,
            false,
            [],
            W,
            Writable,
            "File management",
            "Create folders",
            "Create new folders in the library.",
            since: 5
        ),
        Tenant(
            Rights.RenameFiles,
            false,
            [],
            W,
            Writable,
            "File management",
            "Rename",
            "Rename files and folders. A folder's tags, folder rules and quick links follow it.",
            since: 5
        ),
        Tenant(
            Rights.DeleteFiles,
            false,
            [],
            W,
            Writable,
            "File management",
            "Move to trash and restore",
            "Move files and folders to the space's trash, and restore what you deleted or what is in folders you may "
                + "change.",
            since: 6
        ),
        Tenant(
            Rights.DeletePermanently,
            false,
            [],
            W,
            Writable,
            "File management",
            "Delete permanently",
            "Delete files and folders for good, without the trash, and empty the trash.",
            since: 6
        ),
        Tenant(
            Rights.MoveFiles,
            false,
            [],
            W,
            Writable,
            "File management",
            "Move",
            "Move files and folders to other folders. Their tags, folder rules and quick links follow them.",
            since: 7
        ),
        Tenant(
            Rights.CopyFiles,
            false,
            [],
            W,
            Writable,
            "File management",
            "Copy",
            "Copy files and folders into folders you may change. Only what you may see is copied.",
            since: 7
        ),
        Tenant(
            Rights.UploadFiles,
            false,
            [],
            W,
            Writable,
            "File management",
            "Upload",
            "Upload files and folders from your device into folders you may change.",
            since: 8
        ),
        Tenant(
            Rights.CreateFiles,
            false,
            [],
            W,
            Writable,
            "File management",
            "Create files",
            "Create new empty files in the library, and save copies of text files.",
            since: 9
        ),
        Tenant(
            Rights.ExtractFiles,
            false,
            [],
            W,
            Writable,
            "File management",
            "Extract archives",
            "Extract zip, 7z, rar and tar archives, whole or only some entries, into folders you may change.",
            since: 13
        ),
        Tenant(
            Rights.CompressFiles,
            false,
            [],
            W,
            Writable,
            "File management",
            "Compress",
            "Compress files and folders you may download into a zip, with a password if you want one, saved in a "
                + "folder you may change.",
            since: 14
        ),
        Tenant(
            Rights.ViewTags,
            true,
            Everyone,
            R,
            Readable,
            "Tags",
            "See tags",
            "See the tags of files, the tag list and the tag sidebar."
        ),
        Tenant(
            Rights.AssignTags,
            false,
            Members,
            W,
            Readable,
            "Tags",
            "Tag files",
            "Add tags to files and remove them, also on whole folders and searches."
        ),
        Tenant(
            Rights.ManageTags,
            false,
            Administrators,
            W,
            null,
            "Tags",
            "Manage tags",
            "Create, rename, colour and delete tags, and clear the tag history."
        ),
        Tenant(
            Rights.FolderDefaultTags,
            false,
            Members,
            W,
            Readable,
            "Tags",
            "Set folders' default tags",
            "Give folders tags that every file put into them or their sub-folders gets, whether it is scanned, "
                + "created, uploaded, copied or moved there.",
            since: 12
        ),
        Tenant(
            Rights.Scan,
            false,
            Administrators,
            W,
            Readable,
            "Index",
            "Scan",
            "Start and stop scans of the space's library."
        ),
        Tenant(
            Rights.RemoveFromIndex,
            false,
            Administrators,
            W,
            Readable,
            "Index",
            "Remove folders from the index",
            "Forget a folder's files and tags, and keep scans out of it."
        ),
        Tenant(
            Rights.ExcludeFolders,
            false,
            Administrators,
            W,
            Readable,
            "Index",
            "Exclude folders",
            "Keep folders out of scans, trickplay or thumbnails for the whole space. What is already indexed or made "
                + "stays.",
            since: 10
        ),
        Tenant(
            Rights.Duplicates,
            false,
            Administrators,
            R,
            Readable,
            "Duplicates & moves",
            "See duplicates",
            "List files with the same content."
        ),
        Tenant(
            Rights.RemoveDuplicates,
            false,
            Administrators,
            W,
            Writable,
            "Duplicates & moves",
            "Remove duplicates",
            "Remove a copy of a duplicate file, to the trash unless set otherwise. Its tags go to the copy kept."
        ),
        Tenant(
            Rights.SimilarVideos,
            false,
            Administrators,
            R,
            Readable,
            "Duplicates & moves",
            "Find similar videos",
            "Compare videos and list those that look alike."
        ),
        Tenant(
            Rights.MissingFiles,
            false,
            Administrators,
            W,
            Readable,
            "Duplicates & moves",
            "Find moved files",
            "List tagged files that are no longer found, and relink or forget them."
        ),
        Tenant(
            Rights.ManageTrickplay,
            false,
            Administrators,
            W,
            null,
            "Media",
            "Manage trickplay",
            "See the trickplay activity and start making trickplay."
        ),
        Tenant(
            Rights.ViewTasks,
            false,
            Administrators,
            R,
            null,
            "Background tasks",
            "See background tasks",
            "See what is running, when tasks run next, and their history and logs."
        ),
        Tenant(
            Rights.RunTasks,
            false,
            Administrators,
            W,
            null,
            "Background tasks",
            "Run background tasks",
            "Start and cancel background tasks."
        ),
        Tenant(
            Rights.ScheduleTasks,
            false,
            Administrators,
            W,
            null,
            "Background tasks",
            "Schedule background tasks",
            "Create, change and delete schedules, and clear the task history."
        ),
        Tenant(
            Rights.ViewLogs,
            false,
            Administrators,
            R,
            null,
            "Logs",
            "Read the logs",
            "Read and search the warnings, errors and messages the server logged for the space (server administrators "
                + "see those of the whole server).",
            since: 4
        ),
        Tenant(
            Rights.InterfaceSettings,
            false,
            Administrators,
            W,
            null,
            "Settings",
            "Change interface settings",
            "Change the space's appearance, browsing and search settings."
        ),
        Tenant(
            Rights.PlaybackSettings,
            false,
            Administrators,
            W,
            null,
            "Settings",
            "Change playback settings",
            "Change how the space's video player and external player work."
        ),
        Tenant(
            Rights.ScanningSettings,
            false,
            Administrators,
            W,
            null,
            "Settings",
            "Change scanning settings",
            "Change what scans leave out, the file types they know and what they read in files."
        ),
        Tenant(
            Rights.TagSettings,
            false,
            Administrators,
            W,
            null,
            "Settings",
            "Change tag settings",
            "Change the tag sidebar and its history."
        ),
        Tenant(
            Rights.TrackingSettings,
            false,
            Administrators,
            W,
            null,
            "Settings",
            "Change duplicates & moves settings",
            "Change how duplicates, moved files and similar videos are found."
        ),
        Tenant(
            Rights.ThumbnailSettings,
            false,
            Administrators,
            W,
            null,
            "Settings",
            "Change thumbnail settings",
            "Change how thumbnails of pictures and videos are made."
        ),
        Tenant(
            Rights.TrickplaySettings,
            false,
            Administrators,
            W,
            null,
            "Settings",
            "Change trickplay settings",
            "Change which videos get trickplay, and how it is made."
        ),
        Tenant(
            Rights.TaskSettings,
            false,
            Administrators,
            W,
            null,
            "Settings",
            "Change background task settings",
            "Change how long task logs are kept."
        ),
        Tenant(
            Rights.EditorSettings,
            false,
            Administrators,
            W,
            null,
            "Settings",
            "Change picture editor settings",
            "Change where edited pictures are saved, and their quality.",
            since: 1
        ),
        Tenant(
            Rights.FileSettings,
            false,
            Administrators,
            W,
            null,
            "Settings",
            "Change file management settings",
            "Change which names new files and folders may have, and where the work files go.",
            since: 5
        ),
        Tenant(
            Rights.KeepAliveSettings,
            false,
            Administrators,
            W,
            null,
            "Settings",
            "Change keep-alive settings",
            "Choose if and how often the library's folder is kept alive on its file server, and see the last pings.",
            since: 11
        ),
        Tenant(
            Rights.LogSettings,
            false,
            Administrators,
            W,
            null,
            "Settings",
            "Change log settings",
            "Change how long logs are kept and whether they are also written to files, for the whole server.",
            since: 4
        ),
        Tenant(
            Rights.ManageMembers,
            false,
            Administrators,
            W,
            null,
            "Access",
            "Manage the space's accounts and groups",
            "Create accounts in the space, put them in its groups, choose what each group may do and open the "
                + "space to visitors."
        ),
        Tenant(
            Rights.OwnPreferences,
            false,
            Members,
            W,
            null,
            "Account",
            "Keep your own settings",
            "Keep your own theme, page sizes and player settings on the server."
        ),
        Tenant(
            Rights.OwnPassword,
            false,
            Members,
            W,
            null,
            "Account",
            "Change your password",
            "Change the password of your local account."
        ),
        Tenant(
            Rights.QuickLinks,
            false,
            Members,
            W,
            Readable,
            "Account",
            "Keep quick links",
            "Keep folders and files at hand in the sidebar's quick links.",
            since: 3
        ),
        Tenant(
            Rights.ApiDocs,
            false,
            Administrators,
            R,
            null,
            "Server",
            "Read the API documentation",
            "Open the API's documentation and try its routes."
        ),
        Server(
            Rights.ServerSettings,
            W,
            "Change server settings",
            "Change the settings of the whole server and the defaults of every space."
        ),
        Server(
            Rights.ManageTenants,
            W,
            "Manage spaces",
            "Create, change and delete spaces, and manage the accounts and groups of every space."
        ),
        Server(
            Rights.ManageUsers,
            W,
            "Manage all accounts",
            "Create, change and delete any account, put it in any space and make server administrators."
        ),
        Server(
            Rights.ViewStreams,
            R,
            "See video streams",
            "See the videos the server converts while they play, in every space, and who watches them."
        ),
        .. ConnectionRights.All,
        .. AccessTokenRights.All,
        Server(
            Rights.Backup,
            R,
            "Download backups",
            "Download a backup of the databases and configuration files. It holds password hashes and connection "
                + "strings."
        ),
    ];

    /// <summary>Selector naming every right that changes something (<see cref="RightKind.Write"/>).</summary>
    public const string WriteSelector = "kind:write";

    /// <summary>
    /// Selector naming every right that changes the library's files on disk
    /// (<see cref="RightDefinition.ChangesFiles"/>).
    /// </summary>
    public const string FileWriteSelector = "folder:write";

    /// <summary>
    /// The latest revision of the catalog: one more each time rights are added (their
    /// <see cref="RightDefinition.Since"/>). 0 is the catalog of 1.34.0, the first with rights.
    /// </summary>
    public static int Revision { get; } = All.Max(r => r.Since);

    private static readonly Dictionary<string, RightDefinition> ById = All.ToDictionary(
        r => r.Id,
        StringComparer.Ordinal
    );

    public static IEnumerable<RightDefinition> TenantRights => All.Where(r => r.Scope == RightScope.Tenant);

    public static RightDefinition? Find(string id) => ById.GetValueOrDefault(id);

    public static bool IsTenantRight(string id) => Find(id) is { Scope: RightScope.Tenant };

    /// <summary>
    /// <paramref name="pattern"/> names <paramref name="right"/>: equal (ignoring case), a prefix ending with <c>*</c>
    /// (<c>files.*</c>; <c>*</c> alone names every right), or a selector: <see cref="WriteSelector"/>,
    /// <see cref="FileWriteSelector"/>.
    /// </summary>
    public static bool Matches(string pattern, string right)
    {
        var trimmed = pattern.Trim();
        if (string.Equals(trimmed, WriteSelector, StringComparison.OrdinalIgnoreCase))
            return Find(right) is { Kind: RightKind.Write };
        if (string.Equals(trimmed, FileWriteSelector, StringComparison.OrdinalIgnoreCase))
            return Find(right) is { ChangesFiles: true };
        return MatchesName(trimmed, right);
    }

    public static bool MatchesAny(IEnumerable<string> patterns, string right) => patterns.Any(p => Matches(p, right));

    /// <summary>
    /// <paramref name="pattern"/> names <paramref name="name"/> by its text alone: equal (ignoring case), or a prefix
    /// ending with <c>*</c>. Shared with setting keys (<see cref="Settings.SettingCatalog.Matches"/>).
    /// </summary>
    public static bool MatchesName(string pattern, string name)
    {
        var trimmed = pattern.Trim();
        return trimmed.EndsWith('*')
            ? name.StartsWith(trimmed[..^1], StringComparison.OrdinalIgnoreCase)
            : string.Equals(trimmed, name, StringComparison.OrdinalIgnoreCase);
    }

    private static RightDefinition Tenant(
        string id,
        bool guestAllowed,
        BuiltInGroup[] defaults,
        RightKind kind,
        FolderLevel? atFolder,
        string area,
        string title,
        string description,
        int since = 0
    ) => new(id, RightScope.Tenant, guestAllowed, defaults, area, title, description, since, kind, atFolder);

    private static RightDefinition Server(string id, RightKind kind, string title, string description) =>
        new(id, RightScope.Server, false, [], "Server", title, description, Kind: kind);
}
