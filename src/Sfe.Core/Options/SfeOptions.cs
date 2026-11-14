namespace Otools.Net.Sfe.Core.Options;

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

    /// <summary>
    /// Show files and folders flagged as hidden or system (on Linux and macOS: names starting with a dot).
    /// </summary>
    public bool ShowHiddenItems { get; set; }

    /// <summary>
    /// Portable packages started by hand open the app in the default browser once it listens (never a service). The
    /// command line's <c>--no-browser</c> keeps it closed whatever this says.
    /// </summary>
    public bool OpenBrowser { get; set; } = true;

    /// <summary>
    /// The browser opened at start (<see cref="OpenBrowser"/>) is signed in at once, with a one-time link that lasts a
    /// minute, as the server's only administrator: for an app used on this computer alone. Ignored while the server has
    /// several administrators.
    /// </summary>
    public bool FastSignIn { get; set; }

    /// <summary>
    /// The configuration file the installation page writes (library folder, listening address, ffmpeg), between
    /// <c>appsettings*.json</c> and the environment. Relative paths are resolved against the content root.
    /// </summary>
    public string InstallationFile { get; set; } = "data/installation.json";

    public DatabaseOptions Database { get; set; } = new();
    public BrowseOptions Browse { get; set; } = new();
    public SearchOptions Search { get; set; } = new();
    public ScanOptions Scan { get; set; } = new();
    public KeepAliveOptions KeepAlive { get; set; } = new();
    public FileTypeOptions FileTypes { get; set; } = new();
    public ThumbnailOptions Thumbnails { get; set; } = new();
    public TrickplayOptions Trickplay { get; set; } = new();
    public TagOptions Tags { get; set; } = new();
    public PlayerOptions Player { get; set; } = new();
    public StreamingOptions Streaming { get; set; } = new();
    public FileTrackingOptions FileTracking { get; set; } = new();
    public AppearanceOptions Appearance { get; set; } = new();
    public ReadingOptions Reading { get; set; } = new();
    public TaskOptions Tasks { get; set; } = new();
    public LogOptions Logs { get; set; } = new();
    public EditorOptions Editor { get; set; } = new();
    public FileManagementOptions Files { get; set; } = new();
    public SetupOptions Setup { get; set; } = new();
    public SecurityOptions Security { get; set; } = new();
    public RightOptions Rights { get; set; } = new();
    public TenantOptions Tenants { get; set; } = new();
    public BackupOptions Backup { get; set; } = new();
    public ConnectionOptions Connections { get; set; } = new();

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
