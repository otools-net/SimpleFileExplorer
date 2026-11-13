using Sfe.Core.Connections;

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
