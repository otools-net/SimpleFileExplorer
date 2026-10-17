using Sfe.Core.Data;

namespace Sfe.Core.Access;

/// <summary>
/// Every right of the server. Every function of the API and every setting is covered by one of them (the Api's
/// <c>RequireRight</c>, <see cref="Settings.SettingCatalog"/>); a test fails on a route that names none.
/// </summary>
public static class Rights
{
    public const string Browse = "library.browse";
    public const string ViewFiles = "files.view";
    public const string Download = "files.download";
    public const string ExternalPlayer = "files.external";
    public const string CopyPaths = "files.copyPaths";
    public const string EditImages = "files.edit";
    public const string Stream = "files.stream";
    public const string CreateFolders = "files.createFolders";
    public const string RenameFiles = "files.rename";
    public const string ViewTags = "tags.view";
    public const string AssignTags = "tags.assign";
    public const string ManageTags = "tags.manage";
    public const string Scan = "index.scan";
    public const string RemoveFromIndex = "index.remove";
    public const string Duplicates = "tracking.duplicates";
    public const string RemoveDuplicates = "tracking.duplicatesRemove";
    public const string SimilarVideos = "tracking.similar";
    public const string MissingFiles = "tracking.missing";
    public const string ManageTrickplay = "trickplay.manage";
    public const string ViewTasks = "tasks.view";
    public const string RunTasks = "tasks.run";
    public const string ScheduleTasks = "tasks.schedule";
    public const string ViewLogs = "logs.view";
    public const string InterfaceSettings = "settings.interface";
    public const string PlaybackSettings = "settings.playback";
    public const string ScanningSettings = "settings.scanning";
    public const string TagSettings = "settings.tags";
    public const string TrackingSettings = "settings.tracking";
    public const string ThumbnailSettings = "settings.thumbnails";
    public const string TrickplaySettings = "settings.trickplay";
    public const string TaskSettings = "settings.tasks";
    public const string LogSettings = "settings.logs";
    public const string EditorSettings = "settings.editor";
    public const string FileSettings = "settings.files";
    public const string ManageMembers = "tenant.members";
    public const string OwnPreferences = "account.preferences";
    public const string OwnPassword = "account.password";
    public const string QuickLinks = "account.quickLinks";
    public const string ApiDocs = "system.apiDocs";
    public const string ServerSettings = "server.settings";
    public const string ManageTenants = "server.tenants";
    public const string ManageUsers = "server.users";
    public const string Shutdown = "server.shutdown";
    public const string Dismount = "server.dismount";
    public const string Backup = "server.backup";
    public const string ViewStreams = "server.streams";
}

/// <summary>Space rights are granted to a space's groups; server rights belong to server administrators only.</summary>
public enum RightScope
{
    Tenant,
    Server,
}

/// <summary>What a right does: only looks (<see cref="Read"/>), or changes something through one of its routes.</summary>
public enum RightKind
{
    Read,
    Write,
}

/// <summary>
/// What a folder lets a subject do in it, ordered: <see cref="Write"/> includes <see cref="Read"/>. Read means the
/// folder's files may be seen and used (tagged too, which changes the index, not the files); Write that its files on
/// disk may be changed.
/// </summary>
public enum FolderLevel
{
    Hidden,
    Read,
    Write,
}

/// <param name="GuestAllowed">
/// Guests (visitors without an account and guest accounts) may get it. Off for everything that changes the library,
/// the settings or the accounts, and for what tells about the server: such a right is never theirs, whatever their
/// groups grant.
/// </param>
/// <param name="DefaultGroups">The built-in groups granted it when a space is created; changed by hand afterwards.</param>
/// <param name="Area">Heading the right is listed under on the groups page.</param>
/// <param name="Since">
/// The catalog revision that added it (<see cref="RightCatalog.Revision"/>): the spaces created before get it once, in
/// their built-in groups by <paramref name="DefaultGroups"/> (<see cref="AccessService.GrantNewRightsAsync"/>).
/// </param>
/// <param name="Kind">Write when any route it opens changes something (files, index, tags, settings, accounts).</param>
/// <param name="AtFolder">
/// What a folder must allow for the right to apply in it: <see cref="FolderLevel.Write"/> only for rights that change
/// the library's files on disk; null for a right of the whole space (settings, tasks, accounts).
/// </param>
public sealed record RightDefinition(
    string Id, RightScope Scope, bool GuestAllowed, IReadOnlyList<BuiltInGroup> DefaultGroups, string Area, string Title, string Description, int Since = 0,
    RightKind Kind = RightKind.Read, FolderLevel? AtFolder = null)
{
    /// <summary>Changes the library's files on disk (selector <c>folder:write</c>).</summary>
    public bool ChangesFiles => AtFolder == FolderLevel.Write;
}

public static class RightCatalog
{
    private static readonly BuiltInGroup[] Everyone = [BuiltInGroup.Administrators, BuiltInGroup.Users, BuiltInGroup.Guests];
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
        Tenant(Rights.Browse, true, Everyone, R, Readable, "Library", "Browse and search", "List the folders, search the index, see the scan's progress."),
        Tenant(Rights.ViewFiles, true, Everyone, R, Readable, "Library", "View and play files", "Open pictures, play videos and audio, see thumbnails and trickplay."),
        Tenant(Rights.Download, true, Members, R, Readable, "Library", "Download files", "Save a file to the device."),
        Tenant(Rights.ExternalPlayer, true, Members, R, Readable, "Library", "Open in an external player", "Get a link to a file that works without signing in, for a while, in an app such as VLC."),
        Tenant(Rights.CopyPaths, false, Administrators, R, Readable, "Library", "Copy full paths", "See and copy where files are on the server's disk."),
        Tenant(Rights.EditImages, false, Members, W, Writable, "Library", "Edit pictures", "Crop pictures and save the result as a new file in the folder for edited pictures; originals are never changed.", since: 1),
        Tenant(Rights.Stream, true, Members, R, Readable, "Library", "Play videos converted by the server", "Play videos the browser can't play as they are, converted by ffmpeg on the server while they play (it keeps the server's processor busy).", since: 2),
        // File management (agents/docs/development/file-management-plan.md): nobody gets these by upgrade or by default.
        Tenant(Rights.CreateFolders, false, [], W, Writable, "File management", "Create folders", "Create new folders in the library.", since: 5),
        Tenant(Rights.RenameFiles, false, [], W, Writable, "File management", "Rename", "Rename files and folders (the tags, folder access and quick links of a folder follow it).", since: 5),
        Tenant(Rights.ViewTags, true, Everyone, R, Readable, "Tags", "See tags", "See the tags of files, the tag list and the tag sidebar."),
        Tenant(Rights.AssignTags, false, Members, W, Readable, "Tags", "Tag files", "Add tags to files and remove them, also to whole folders and searches."),
        Tenant(Rights.ManageTags, false, Administrators, W, null, "Tags", "Manage tags", "Create, rename, color and delete tags; clear the tag history."),
        Tenant(Rights.Scan, false, Administrators, W, Readable, "Index", "Scan", "Start and stop scans of the space's library."),
        Tenant(Rights.RemoveFromIndex, false, Administrators, W, Readable, "Index", "Remove folders from the index", "Forget a folder's files and tags, and keep scans out of it."),
        Tenant(Rights.Duplicates, false, Administrators, R, Readable, "Duplicates & moves", "See duplicates", "List identical files."),
        Tenant(Rights.RemoveDuplicates, false, Administrators, W, Writable, "Duplicates & moves", "Delete duplicates", "Delete a copy of a file from the disk (its tags go to the copy kept)."),
        Tenant(Rights.SimilarVideos, false, Administrators, R, Readable, "Duplicates & moves", "Similar videos", "Compare videos and list the similar ones."),
        Tenant(Rights.MissingFiles, false, Administrators, W, Readable, "Duplicates & moves", "Moved files", "List tagged files no longer found, relink and forget them."),
        Tenant(Rights.ManageTrickplay, false, Administrators, W, null, "Media", "Trickplay activity", "See and start the trickplay generation."),
        Tenant(Rights.ViewTasks, false, Administrators, R, null, "Background tasks", "See background tasks", "See what runs, when tasks run next, and their history and logs."),
        Tenant(Rights.RunTasks, false, Administrators, W, null, "Background tasks", "Run background tasks", "Start and cancel background tasks."),
        Tenant(Rights.ScheduleTasks, false, Administrators, W, null, "Background tasks", "Schedule background tasks", "Create, change and delete schedules; clear the history."),
        Tenant(Rights.ViewLogs, false, Administrators, R, null, "Logs", "See the logs", "Read and search the warnings, errors and messages the server logged for the space (server administrators see those of the whole server).", since: 4),
        Tenant(Rights.InterfaceSettings, false, Administrators, W, null, "Settings", "Interface settings", "Appearance, browsing and search settings of the space."),
        Tenant(Rights.PlaybackSettings, false, Administrators, W, null, "Settings", "Playback settings", "The video player and the external player of the space."),
        Tenant(Rights.ScanningSettings, false, Administrators, W, null, "Settings", "Scanning settings", "What scans leave out, file types, what scans read."),
        Tenant(Rights.TagSettings, false, Administrators, W, null, "Settings", "Tag settings", "The tag sidebar and its history."),
        Tenant(Rights.TrackingSettings, false, Administrators, W, null, "Settings", "Duplicates & moves settings", "Duplicates, moved files and similar videos."),
        Tenant(Rights.ThumbnailSettings, false, Administrators, W, null, "Settings", "Thumbnail settings", "Thumbnails of photos and videos."),
        Tenant(Rights.TrickplaySettings, false, Administrators, W, null, "Settings", "Trickplay settings", "Which videos get trickplay and how."),
        Tenant(Rights.TaskSettings, false, Administrators, W, null, "Settings", "Background task settings", "How long task logs are kept."),
        Tenant(Rights.EditorSettings, false, Administrators, W, null, "Settings", "Picture editor settings", "Where edited pictures are saved, and their quality.", since: 1),
        Tenant(Rights.FileSettings, false, Administrators, W, null, "Settings", "File management settings", "Which names new files and folders may have, and where the work files go.", since: 5),
        Tenant(Rights.LogSettings, false, Administrators, W, null, "Settings", "Log settings", "How long the logs are kept, and whether they are also written to files (both set for the whole server).", since: 4),
        Tenant(Rights.ManageMembers, false, Administrators, W, null, "Access", "Manage the space's accounts and groups", "Create accounts in the space, put them in its groups, choose what each group may do, open the space to visitors."),
        Tenant(Rights.OwnPreferences, false, Members, W, null, "Account", "Own preferences", "Keep one's own theme, page sizes and player preferences on the server."),
        Tenant(Rights.OwnPassword, false, Members, W, null, "Account", "Change own password", "Change the password of one's own local account."),
        Tenant(Rights.QuickLinks, false, Members, W, Readable, "Account", "Quick links", "Keep folders and files at hand in the sidebar's quick links.", since: 3),
        Tenant(Rights.ApiDocs, false, Administrators, R, null, "Server", "API documentation", "Open the API's documentation and try its routes."),
        Server(Rights.ServerSettings, W, "Server settings", "Settings of the whole server and the defaults of every space."),
        Server(Rights.ManageTenants, W, "Spaces", "Create, change and delete spaces, and manage the accounts and groups of every space."),
        Server(Rights.ManageUsers, W, "All accounts", "Create, change and delete every account, put it in any space, make server administrators."),
        Server(Rights.Shutdown, W, "Shut down", "Shut the server's computer down (when the server allows it)."),
        Server(Rights.Dismount, W, "Dismount", "Run the server's dismount command (when one is set)."),
        Server(Rights.ViewStreams, R, "Video streams", "See the videos the server converts while they play, in every space, and who watches them."),
        Server(Rights.Backup, R, "Back up", "Download a backup of the databases and configuration files (it holds password hashes and connection strings)."),
    ];

    /// <summary>Selector naming every right that changes something (<see cref="RightKind.Write"/>).</summary>
    public const string WriteSelector = "kind:write";

    /// <summary>Selector naming every right that changes the library's files on disk (<see cref="RightDefinition.ChangesFiles"/>).</summary>
    public const string FileWriteSelector = "folder:write";

    /// <summary>
    /// The latest revision of the catalog: one more each time rights are added (their <see cref="RightDefinition.Since"/>).
    /// 0 is the catalog of 1.34.0, the first with rights.
    /// </summary>
    public static int Revision { get; } = All.Max(r => r.Since);

    private static readonly Dictionary<string, RightDefinition> ById = All.ToDictionary(r => r.Id, StringComparer.Ordinal);

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
        string id, bool guestAllowed, BuiltInGroup[] defaults, RightKind kind, FolderLevel? atFolder, string area, string title, string description, int since = 0) =>
        new(id, RightScope.Tenant, guestAllowed, defaults, area, title, description, since, kind, atFolder);

    private static RightDefinition Server(string id, RightKind kind, string title, string description) =>
        new(id, RightScope.Server, false, [], "Server", title, description, Kind: kind);
}
