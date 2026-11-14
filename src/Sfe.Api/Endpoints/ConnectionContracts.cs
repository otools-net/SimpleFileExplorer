using Microsoft.AspNetCore.Mvc;
using Sfe.Api.Security;
using Sfe.Core.Access;
using Sfe.Core.Connections;
using Sfe.Core.Library;

namespace Sfe.Api.Endpoints;

/// <summary>How a kind of connection is set up on this server (Administration › Connections).</summary>
/// <param name="Offered">Allowed by the settings and configured enough to sign in (Dropbox: an app key).</param>
/// <param name="ReturnAddress">What to register at the provider for the redirect sign-in, as this request reached
/// the server.</param>
/// <param name="AppSecret">For a provider with a registered app: <c>none</c>, <c>page</c> (typed on the page) or
/// <c>configuration</c> (which wins); null for other kinds.</param>
public sealed record ProviderSetupView(string Kind, string Name, bool Offered, string ReturnAddress, string? AppSecret)
{
    public const string NoSecret = "none";
    public const string SecretOnPage = "page";
    public const string SecretInConfiguration = "configuration";
}

/// <summary>A provider app's secret, as typed.</summary>
public sealed record AppSecretInput(string? Secret);

/// <summary>What the person brings back to end a sign-in (the code the provider showed).</summary>
public sealed record SignInCompletionInput(string? State, Dictionary<string, string>? Fields);

/// <summary>What the provider's redirect brings back (bound with <c>[AsParameters]</c>).</summary>
/// <param name="Error">Set when the person refused at the provider, or it failed.</param>
internal sealed record OAuthCallback(string? Code, string? State, string? Error, HttpRequest Request);

/// <summary>The connections of a session (<c>GET api/connections</c>).</summary>
/// <param name="Usable">Those it may use in its library, and how far.</param>
/// <param name="Managed">Those it manages, by name.</param>
/// <param name="Groups">The groups of its library, to give connections to (<c>connections.library</c>; else
/// empty).</param>
/// <param name="Libraries">Every library, to offer server connections to (<c>connections.server</c>; else
/// empty).</param>
public sealed record ConnectionsView(
    IReadOnlyList<UsableConnection> Usable,
    IReadOnlyList<ManagedConnection> Managed,
    IReadOnlyList<NamedItem> Groups,
    IReadOnlyList<NamedItem> Libraries
);

/// <summary>Something to pick by name (a group, a library).</summary>
public sealed record NamedItem(int Id, string Name);

/// <summary>A connection the session manages, and what it may do with it.</summary>
/// <param name="Manage">Rename, switch, sign in again (an account's own, or its level's right).</param>
/// <param name="Remove">Remove (also the server's administrators for an account's connection).</param>
public sealed record ManagedConnection(Sfe.Core.Connections.ConnectionInfo Connection, bool Manage, bool Remove);

/// <summary>The libraries a server-level connection is offered to.</summary>
/// <param name="AllLibraries">Every library, those made later too.</param>
public sealed record ConnectionLibrariesInput(bool AllLibraries, int[]? LibraryIds);

/// <summary>The groups of the request's library using a connection, with their level (Read or Write).</summary>
public sealed record ConnectionGroupsInput(Dictionary<int, Sfe.Core.Access.FolderLevel>? Groups);

/// <summary>A page of a connection's folder (<c>GET api/connections/{id}/browse</c>).</summary>
public sealed record ConnectionBrowseQuery(
    [FromQuery(Name = "path")] string? Path = null,
    [FromQuery(Name = "offset")] int Offset = 0,
    [FromQuery(Name = "limit")] int? Limit = null,
    [FromQuery(Name = "sort")] string? Sort = null,
    [FromQuery(Name = "desc")] bool Desc = false
);

/// <summary>A page of a connection's folder: the library's listing, and what the connection can do.</summary>
/// <param name="Listing">As <c>GET api/browse</c> gives it; paths are relative to the connection's root folder,
/// whose breadcrumb is the connection's name. While <paramref name="HasMore"/>, <c>total</c> counts what was read so
/// far.</param>
/// <param name="Access"><c>read</c> or <c>write</c>: how far the session uses the connection.</param>
/// <param name="Sorted">The sort asked for was applied; false for a folder too large to read whole (the provider's
/// order).</param>
/// <param name="HasMore">The folder has more items than <c>total</c>: the next pages read them.</param>
public sealed record ConnectionListing(
    int ConnectionId,
    string Name,
    string Kind,
    DirectoryListing Listing,
    FolderLevel Access,
    bool Sorted,
    bool HasMore,
    ProviderCapabilities Capabilities
);

/// <summary>A connection opened for one request (<see cref="ConnectionFileServices.OpenAsync"/>).</summary>
internal sealed record OpenConnection(
    UsableConnection Connection,
    IRemoteStorage Storage,
    ProviderCapabilities Capabilities
);

/// <summary>What the listing of a connection's folder uses (bound with <c>[AsParameters]</c>).</summary>
internal sealed record ConnectionBrowseServices(
    RemoteListings Listings,
    MediaClassifier Classifier,
    Microsoft.Extensions.Options.IOptionsMonitor<Sfe.Core.Options.SfeOptions> Options
);

/// <summary>What the zips of a connection's files use (bound with <c>[AsParameters]</c>).</summary>
internal sealed record ConnectionArchiveServices(
    ArchiveLinks Links,
    Sfe.Core.Files.Archives Archives,
    ILogger<Sfe.Core.Files.Archives> Logger
);

/// <summary>What the routes changing a connection's files use besides the connection (bound with
/// <c>[AsParameters]</c>).</summary>
internal sealed record ConnectionWriteServices(RemoteListings Listings, MediaClassifier Classifier);

/// <summary>Files and folders of a connection moved or copied into one of its folders.</summary>
/// <param name="Kind"><c>move</c> or <c>copy</c>.</param>
/// <param name="Paths">The items, relative to the connection's root folder.</param>
/// <param name="Target">The folder they go into.</param>
/// <param name="Conflict"><c>fail</c> (default), <c>keepBoth</c> (the provider's numbering) or <c>replace</c> (a
/// file only, never a folder).</param>
public sealed record ConnectionTransferRequest(
    string? Kind,
    IReadOnlyList<string>? Paths,
    string? Target,
    string? Conflict = null
);

/// <summary>What became of each item of a <see cref="ConnectionTransferRequest"/>, in its order.</summary>
public sealed record ConnectionTransferResults(IReadOnlyList<ConnectionTransferItem> Items);

/// <param name="Item">The item where it is now, when <paramref name="Status"/> is Done.</param>
public sealed record ConnectionTransferItem(
    string Path,
    Sfe.Core.Files.FileOperationStatus Status,
    string? Message = null,
    LibraryItem? Item = null
);

/// <summary>One move or copy of a <see cref="ConnectionTransferRequest"/>, as checked.</summary>
internal sealed record ConnectionTransferStep(
    IRemoteStorage Storage,
    string Target,
    RemoteConflict Conflict,
    bool Move,
    StringComparison Comparison,
    MediaClassifier Classifier
);

/// <summary>What the uploads into a connection use besides the connection (bound with <c>[AsParameters]</c>).</summary>
internal sealed record ConnectionUploadServices(
    RemoteUploads Uploads,
    RemoteListings Listings,
    MediaClassifier Classifier,
    Microsoft.Extensions.Options.IOptionsMonitor<Sfe.Core.Options.SfeOptions> Options
);

/// <summary>An upload into a connection being changed by a request, its gate held.</summary>
internal sealed record ConnectionUploadStep(
    ConnectionFileServices Files,
    OpenConnection Open,
    ConnectionUploadServices Services,
    RemoteUpload Upload
);
