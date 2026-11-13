using Sfe.Api.Security;
using Sfe.Core.Connections;
using Sfe.Core.Options;
using Sfe.Core.Tagging;

namespace Sfe.Api.Endpoints;

/// <param name="Trickplay">Videos may have trickplay (see <c>/api/trickplay/files/{path}</c>).</param>
public sealed record ClientFeatures(bool Thumbnails, bool Trickplay);

/// <param name="Name">Shown on its button, e.g. "Open in VLC".</param>
/// <param name="Url">
/// Link template: <c>{url}</c> = the file's full address, <c>{encodedUrl}</c> = the same percent-encoded; without
/// either the address is appended.
/// </param>
public sealed record ExternalPlayer(string Name, string Url);

/// <summary>Smallest shorter side, in pixels, of a high-resolution image and of a high-resolution video.</summary>
public sealed record ResolutionThresholds(int Image, int Video);

/// <param name="OutputFolder">Library folder edited pictures are saved in, under their originals' folders; empty = next
/// to the originals.</param>
/// <param name="Quality">Quality (1-100) of the JPEG and WebP pictures the editor encodes.</param>
public sealed record EditorConfig(string OutputFolder, int Quality);

/// <param name="Always">Every video plays converted (<c>Streaming:Mode</c> = <c>Always</c>), not only those the browser
/// can't play.</param>
public sealed record StreamingConfig(bool Always);

/// <param name="DeletePermanently">Every delete skips the trash (<c>Files:DeletePermanently</c>).</param>
/// <param name="TrashRetentionDays">Days in the trash before the purge; 0 = until emptied.</param>
/// <param name="MaxUploadMegabytes">The largest file one may upload; 0 = no limit.</param>
/// <param name="KeepUnfinishedUploadsHours">Hours an unfinished upload is kept without receiving anything.</param>
public sealed record FilesConfig(
    bool DeletePermanently,
    int TrashRetentionDays,
    int MaxUploadMegabytes = 0,
    int KeepUnfinishedUploadsHours = 48
);

/// <summary>A kind of connection that can be made now, and how its sign-in form is built.</summary>
public sealed record ConnectionKind(
    string Kind,
    string Name,
    string Icon,
    IReadOnlyList<SignInMethod> SignIn,
    ProviderCapabilities Capabilities
);

/// <param name="Levels">The levels connections may be made at (<c>account</c>, <c>library</c>, <c>server</c>).</param>
/// <param name="Kinds">Empty until whoever runs the server has configured one (Dropbox: its app key).</param>
public sealed record ConnectionsConfig(IReadOnlyList<string> Levels, IReadOnlyList<ConnectionKind> Kinds);

/// <param name="DataServer">When set, the client links files to this server instead of <c>/api/files/raw</c>: a base
/// the path is appended to, or a template with <c>{url}</c> / <c>{encodedUrl}</c>
/// (<see cref="DataServerUrl"/>).</param>
/// <param name="PageSize">Items the client loads at a time while browsing a folder.</param>
/// <param name="SearchPageSize">Results the client loads at a time while searching.</param>
/// <param name="TagColors">Palette for tag colors, see <see cref="Sfe.Core.Tagging.TagColors"/>.</param>
/// <param name="MaxFilesPerTagChange">Files one tag change may name: the client splits larger selections.</param>
/// <param name="LargeImageSize">Images larger than this (bytes) are previewed through their thumbnail only.</param>
/// <param name="SeekSeconds">Seconds the video player jumps back or forward.</param>
/// <param name="Loop">Videos start over at their end until the player's loop button is switched off.</param>
/// <param name="TrickplayHover">Hovering a video in a listing plays its trickplay over the thumbnail.</param>
/// <param name="FolderPictures">Pictures on a folder card (see <c>/api/browse/covers</c>); 0 = folders without
/// pictures.</param>
/// <param name="SearchExplain">The search box offers to put the query into words.</param>
/// <param name="SearchHistory">Searches each browser remembers and suggests in the search box; 0 = none.</param>
/// <param name="SingleViewOriginals">The "one per row" view shows images themselves, not their thumbnails.</param>
/// <param name="ShowResolution">Listings and the viewer show the resolution of images and videos.</param>
/// <param name="ShowDuration">Listings and the viewer show the duration of videos and audio files.</param>
/// <param name="ResolutionMark">
/// Thresholds of the HR/LR marks on the previews of images and videos; null = no marks (<c>Browse:ResolutionMark</c>).
/// </param>
/// <param name="RootPath">
/// Full path of the library root on the server, ending with its directory separator, for the buttons copying full
/// paths; null when they are off (<c>Browse:CopyPaths</c>), so that the server's layout is not given away.
/// </param>
/// <param name="ExternalPlayer">App that videos and audio can be opened in (<c>Player:ExternalUrl</c>); null =
/// none.</param>
/// <param name="Theme">Theme of the browsers that have not picked one themselves.</param>
/// <param name="Version">Version of the server (<c>Directory.Build.props</c>), e.g. <c>1.2.0</c>.</param>
/// <param name="Commit">Short hash of the commit it was built from, when known.</param>
/// <param name="Rights">What the session may do in the library (<see cref="Sfe.Core.Access.Rights"/>): the client hides
/// the rest.</param>
/// <param name="User">The signed-in account; null for a visitor without one.</param>
/// <param name="Tenant">The library being browsed.</param>
/// <param name="Tenants">The libraries the account can switch to (empty for a visitor without one).</param>
/// <param name="Editor">The picture editor's settings; null without the right to edit pictures.</param>
/// <param name="Streaming">
/// Videos the browser can't play are converted by the server (<c>/api/streaming/play</c>); null when streaming is off,
/// ffmpeg is missing, or without the right <c>files.stream</c>.
/// </param>
/// <param name="Flash">Flash movies play in the viewer (<c>Player:Flash</c>, and the right <c>files.view</c>).</param>
/// <param name="Archives">Which archives the viewer explores; null without the right <c>files.view</c>.</param>
/// <param name="Connections">
/// Connections to other storage; null when they are off (<c>Connections:Enabled</c>) or the session has none of the
/// <c>connections.*</c> rights.
/// </param>
public sealed record ClientConfig(
    string RootName,
    string? DataServer,
    int PageSize,
    int SearchPageSize,
    ClientFeatures Features,
    IReadOnlyList<TagColor> TagColors,
    int MaxFilesPerTagChange,
    long LargeImageSize,
    int SeekSeconds,
    bool Loop,
    bool TrickplayHover,
    int FolderPictures,
    bool SearchExplain,
    int SearchHistory,
    bool SingleViewOriginals,
    bool AnimatedHover,
    bool ShowResolution,
    bool ShowDuration,
    ResolutionThresholds? ResolutionMark,
    string? RootPath,
    ExternalPlayer? ExternalPlayer,
    Theme Theme,
    string Version,
    string? Commit,
    IReadOnlyCollection<string> Rights,
    SessionAccount? User,
    SessionTenant? Tenant,
    IReadOnlyList<SessionTenant> Tenants,
    EditorConfig? Editor,
    StreamingConfig? Streaming,
    FilesConfig? Files = null,
    ReadingOptions? Reading = null,
    IReadOnlyList<string>? TextExtensions = null,
    ServerMode Mode = ServerMode.Local,
    bool Flash = false,
    ArchivesConfig? Archives = null,
    ConnectionsConfig? Connections = null
);

/// <param name="MaxCompressedTarMegabytes">Compressed tars (<c>.tar.gz</c>…) larger than this aren't explored (listing
/// one decompresses all of it): the client offers no viewer for them.</param>
public sealed record ArchivesConfig(int MaxCompressedTarMegabytes);
