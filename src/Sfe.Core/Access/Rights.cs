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
    public const string DeleteFiles = "files.delete";
    public const string DeletePermanently = "files.deletePermanently";
    public const string MoveFiles = "files.move";
    public const string CopyFiles = "files.copy";
    public const string UploadFiles = "files.upload";
    public const string CreateFiles = "files.createFiles";
    public const string ExtractFiles = "files.extract";
    public const string CompressFiles = "files.compress";
    public const string ViewTags = "tags.view";
    public const string AssignTags = "tags.assign";
    public const string ManageTags = "tags.manage";
    public const string FolderDefaultTags = "tags.folderDefaults";
    public const string Scan = "index.scan";
    public const string RemoveFromIndex = "index.remove";
    public const string ExcludeFolders = "index.exclude";
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
    public const string KeepAliveSettings = "settings.keepAlive";
    public const string ManageMembers = "tenant.members";
    public const string OwnPreferences = "account.preferences";
    public const string OwnPassword = "account.password";
    public const string QuickLinks = "account.quickLinks";
    public const string ApiDocs = "system.apiDocs";
    public const string ServerSettings = "server.settings";
    public const string ManageTenants = "server.tenants";
    public const string ManageUsers = "server.users";
    public const string Backup = "server.backup";
    public const string ViewStreams = "server.streams";

    /// <summary>See and open the connections one is allowed to use (agents/docs/features/connections.md).</summary>
    public const string UseConnections = "connections.use";

    /// <summary>Connect storage of one's own, private to the account.</summary>
    public const string OwnConnections = "connections.own";

    /// <summary>Make and manage the connections of the space, and say which of its groups use the server's.</summary>
    public const string LibraryConnections = "connections.library";

    /// <summary>Make and manage the server's connections and offer them to spaces.</summary>
    public const string ServerConnections = "connections.server";
}
