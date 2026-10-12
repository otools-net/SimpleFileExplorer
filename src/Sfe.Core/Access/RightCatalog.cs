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
    public const string InterfaceSettings = "settings.interface";
    public const string PlaybackSettings = "settings.playback";
    public const string ScanningSettings = "settings.scanning";
    public const string TagSettings = "settings.tags";
    public const string TrackingSettings = "settings.tracking";
    public const string ThumbnailSettings = "settings.thumbnails";
    public const string TrickplaySettings = "settings.trickplay";
    public const string TaskSettings = "settings.tasks";
    public const string ManageMembers = "tenant.members";
    public const string OwnPreferences = "account.preferences";
    public const string OwnPassword = "account.password";
    public const string ApiDocs = "system.apiDocs";
    public const string ServerSettings = "server.settings";
    public const string ManageTenants = "server.tenants";
    public const string ManageUsers = "server.users";
    public const string Shutdown = "server.shutdown";
    public const string Dismount = "server.dismount";
}

/// <summary>Space rights are granted to a space's groups; server rights belong to server administrators only.</summary>
public enum RightScope
{
    Tenant,
    Server,
}

/// <param name="GuestAllowed">
/// Guests (visitors without an account and guest accounts) may get it. Off for everything that changes the library,
/// the settings or the accounts, and for what tells about the server: such a right is never theirs, whatever their
/// groups grant.
/// </param>
/// <param name="DefaultGroups">The built-in groups granted it when a space is created; changed by hand afterwards.</param>
/// <param name="Area">Heading the right is listed under on the groups page.</param>
public sealed record RightDefinition(
    string Id, RightScope Scope, bool GuestAllowed, IReadOnlyList<BuiltInGroup> DefaultGroups, string Area, string Title, string Description);

public static class RightCatalog
{
    private static readonly BuiltInGroup[] Everyone = [BuiltInGroup.Administrators, BuiltInGroup.Users, BuiltInGroup.Guests];
    private static readonly BuiltInGroup[] Members = [BuiltInGroup.Administrators, BuiltInGroup.Users];
    private static readonly BuiltInGroup[] Administrators = [BuiltInGroup.Administrators];

    public static IReadOnlyList<RightDefinition> All { get; } =
    [
        Tenant(Rights.Browse, true, Everyone, "Library", "Browse and search", "List the folders, search the index, see the scan's progress."),
        Tenant(Rights.ViewFiles, true, Everyone, "Library", "View and play files", "Open pictures, play videos and audio, see thumbnails and trickplay."),
        Tenant(Rights.Download, true, Members, "Library", "Download files", "Save a file to the device."),
        Tenant(Rights.ExternalPlayer, true, Members, "Library", "Open in an external player", "Get a link to a file that works without signing in, for a while, in an app such as VLC."),
        Tenant(Rights.CopyPaths, false, Administrators, "Library", "Copy full paths", "See and copy where files are on the server's disk."),
        Tenant(Rights.ViewTags, true, Everyone, "Tags", "See tags", "See the tags of files, the tag list and the tag sidebar."),
        Tenant(Rights.AssignTags, false, Members, "Tags", "Tag files", "Add tags to files and remove them, also to whole folders and searches."),
        Tenant(Rights.ManageTags, false, Administrators, "Tags", "Manage tags", "Create, rename, color and delete tags; clear the tag history."),
        Tenant(Rights.Scan, false, Administrators, "Index", "Scan", "Start and stop scans of the space's library."),
        Tenant(Rights.RemoveFromIndex, false, Administrators, "Index", "Remove folders from the index", "Forget a folder's files and tags, and keep scans out of it."),
        Tenant(Rights.Duplicates, false, Administrators, "Duplicates & moves", "See duplicates", "List identical files."),
        Tenant(Rights.RemoveDuplicates, false, Administrators, "Duplicates & moves", "Delete duplicates", "Delete a copy of a file from the disk (its tags go to the copy kept)."),
        Tenant(Rights.SimilarVideos, false, Administrators, "Duplicates & moves", "Similar videos", "Compare videos and list the similar ones."),
        Tenant(Rights.MissingFiles, false, Administrators, "Duplicates & moves", "Moved files", "List tagged files no longer found, relink and forget them."),
        Tenant(Rights.ManageTrickplay, false, Administrators, "Media", "Trickplay activity", "See and start the trickplay generation."),
        Tenant(Rights.ViewTasks, false, Administrators, "Background tasks", "See background tasks", "See what runs, when tasks run next, and their history and logs."),
        Tenant(Rights.RunTasks, false, Administrators, "Background tasks", "Run background tasks", "Start and cancel background tasks."),
        Tenant(Rights.ScheduleTasks, false, Administrators, "Background tasks", "Schedule background tasks", "Create, change and delete schedules; clear the history."),
        Tenant(Rights.InterfaceSettings, false, Administrators, "Settings", "Interface settings", "Appearance, browsing and search settings of the space."),
        Tenant(Rights.PlaybackSettings, false, Administrators, "Settings", "Playback settings", "The video player and the external player of the space."),
        Tenant(Rights.ScanningSettings, false, Administrators, "Settings", "Scanning settings", "What scans leave out, file types, what scans read."),
        Tenant(Rights.TagSettings, false, Administrators, "Settings", "Tag settings", "The tag sidebar and its history."),
        Tenant(Rights.TrackingSettings, false, Administrators, "Settings", "Duplicates & moves settings", "Duplicates, moved files and similar videos."),
        Tenant(Rights.ThumbnailSettings, false, Administrators, "Settings", "Thumbnail settings", "Thumbnails of photos and videos."),
        Tenant(Rights.TrickplaySettings, false, Administrators, "Settings", "Trickplay settings", "Which videos get trickplay and how."),
        Tenant(Rights.TaskSettings, false, Administrators, "Settings", "Background task settings", "How long task logs are kept."),
        Tenant(Rights.ManageMembers, false, Administrators, "Access", "Manage the space's accounts and groups", "Create accounts in the space, put them in its groups, choose what each group may do, open the space to visitors."),
        Tenant(Rights.OwnPreferences, false, Members, "Account", "Own preferences", "Keep one's own theme, page sizes and player preferences on the server."),
        Tenant(Rights.OwnPassword, false, Members, "Account", "Change own password", "Change the password of one's own local account."),
        Tenant(Rights.ApiDocs, false, Administrators, "Server", "API documentation", "Open the API's documentation and try its routes."),
        Server(Rights.ServerSettings, "Server settings", "Settings of the whole server and the defaults of every space."),
        Server(Rights.ManageTenants, "Spaces", "Create, change and delete spaces, and manage the accounts and groups of every space."),
        Server(Rights.ManageUsers, "All accounts", "Create, change and delete every account, put it in any space, make server administrators."),
        Server(Rights.Shutdown, "Shut down", "Shut the server's computer down (when the server allows it)."),
        Server(Rights.Dismount, "Dismount", "Run the server's dismount command (when one is set)."),
    ];

    private static readonly Dictionary<string, RightDefinition> ById = All.ToDictionary(r => r.Id, StringComparer.Ordinal);

    public static IEnumerable<RightDefinition> TenantRights => All.Where(r => r.Scope == RightScope.Tenant);

    public static RightDefinition? Find(string id) => ById.GetValueOrDefault(id);

    public static bool IsTenantRight(string id) => Find(id) is { Scope: RightScope.Tenant };

    /// <summary>
    /// <paramref name="pattern"/> names <paramref name="right"/>: equal (ignoring case), or a prefix ending with
    /// <c>*</c> (<c>files.*</c>; <c>*</c> alone names every right).
    /// </summary>
    public static bool Matches(string pattern, string right)
    {
        var trimmed = pattern.Trim();
        return trimmed.EndsWith('*')
            ? right.StartsWith(trimmed[..^1], StringComparison.OrdinalIgnoreCase)
            : string.Equals(trimmed, right, StringComparison.OrdinalIgnoreCase);
    }

    public static bool MatchesAny(IEnumerable<string> patterns, string right) => patterns.Any(p => Matches(p, right));

    private static RightDefinition Tenant(string id, bool guestAllowed, BuiltInGroup[] defaults, string area, string title, string description) =>
        new(id, RightScope.Tenant, guestAllowed, defaults, area, title, description);

    private static RightDefinition Server(string id, string title, string description) =>
        new(id, RightScope.Server, false, [], "Server", title, description);
}
