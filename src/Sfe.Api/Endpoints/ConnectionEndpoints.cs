using Microsoft.AspNetCore.Http.HttpResults;
using Sfe.Api.Security;
using Sfe.Core.Access;
using Sfe.Core.Connections;
using ConnectionInfo = Sfe.Core.Connections.ConnectionInfo;

namespace Sfe.Api.Endpoints;

/// <summary>
/// The connections a session uses and those it manages (<see cref="ConnectionManagers"/>): listed, renamed, turned
/// off, made read only, removed, offered to libraries, given to groups. Making one is
/// <see cref="ConnectionSignInEndpoints"/>; a connection one may not manage answers 404.
/// </summary>
public static class ConnectionEndpoints
{
    public static RouteGroupBuilder MapConnectionEndpoints(this RouteGroupBuilder api)
    {
        var connections = api.MapGroup("/connections").WithTags("Connections");

        connections
            .MapGet("/", List)
            .RequireRight([Rights.UseConnections, .. ConnectionManagers.Rights])
            .WithName("GetConnections")
            .WithSummary(
                "The connections this session may use in its library (and how far), and those it manages: its own, "
                    + "its library's, the server's; the server's administrators also see every account's, to remove."
            );

        connections
            .MapPatch("/{id:int}", Update)
            .RequireRight(ConnectionManagers.Rights)
            .WithName("UpdateConnection")
            .WithSummary(
                "Renames a connection, turns it off or on, makes it read only, or roots it at another folder."
            );

        connections
            .MapDelete("/{id:int}", Remove)
            .RequireRight(ConnectionManagers.Rights)
            .WithName("RemoveConnection")
            .WithSummary("Removes a connection with who uses it; the files at the provider are left as they are.");

        connections
            .MapPut("/{id:int}/libraries", SetLibraries)
            .RequireRight(Rights.ServerConnections)
            .WithName("SetConnectionLibraries")
            .WithSummary(
                "Offers a server-level connection to every library or to some; a library no longer offered loses "
                    + "its groups' access."
            );

        connections
            .MapPut("/{id:int}/groups", SetGroups)
            .RequireRight(Rights.LibraryConnections)
            .WithName("SetConnectionGroups")
            .WithSummary(
                "The groups of the request's library that use a connection of that library, or a server-level one "
                    + "offered to it: Read or Write, replacing those it had there."
            );

        return api;
    }

    private static async Task<Ok<ConnectionsView>> List(
        [AsParameters] ConnectionListServices services,
        CancellationToken cancellationToken
    )
    {
        var (current, connections, access, accounts) = services;
        var state = await current.GetAsync();
        var tenants = await accounts.GetCachedTenantsAsync(cancellationToken);
        var usable = await access.UsableAsync(SubjectOf(state), cancellationToken);
        var managed = new Dictionary<int, ConnectionInfo>();
        if (state.User is { } user && state.Has(Rights.OwnConnections))
            Add(await connections.GetOwnedAsync(user.Id, cancellationToken));
        if (state.Tenant is { } tenant && state.Has(Rights.LibraryConnections))
            Add(await connections.GetOfLibraryAsync(tenant.Id, cancellationToken));
        if (state.Has(Rights.ServerConnections))
            Add(await connections.GetAtLevelAsync(ConnectionLevel.Server, cancellationToken));
        if (state.User is { IsServerAdmin: true })
            Add(await connections.GetAtLevelAsync(ConnectionLevel.Account, cancellationToken));

        var views = managed
            .Values.OrderBy(c => c.Name, StringComparer.CurrentCultureIgnoreCase)
            .Select(c => new ManagedConnection(
                c,
                ConnectionManagers.MayManage(state, c),
                ConnectionManagers.MayRemove(state, c)
            ))
            .ToList();
        return TypedResults.Ok(
            new ConnectionsView(usable, views, await GroupsAsync(state, accounts, cancellationToken), Libraries())
        );

        IReadOnlyList<NamedItem> Libraries() =>
            state.Has(Rights.ServerConnections)
                ? [.. tenants.Where(t => !t.Disabled).Select(t => new NamedItem(t.Id, t.Name))]
                : [];

        // A server-level one may come twice (offered to the library, and the server's): the library's view first.
        void Add(IEnumerable<ConnectionInfo> list)
        {
            foreach (var connection in list)
                managed.TryAdd(connection.Id, connection);
        }
    }

    private static async Task<Results<Ok<ConnectionInfo>, ValidationProblem, NotFound>> Update(
        int id,
        ConnectionChange change,
        CurrentAccess current,
        ConnectionService connections,
        CancellationToken cancellationToken
    )
    {
        var state = await current.GetAsync();
        var connection = await connections.FindAsync(id, cancellationToken);
        if (connection is null || !ConnectionManagers.MayManage(state, connection))
            return TypedResults.NotFound();

        var changed = await connections.UpdateAsync(id, change, state.User?.Id, cancellationToken);
        return changed.Status switch
        {
            AccessStatus.Ok => TypedResults.Ok(changed.Value!),
            AccessStatus.Invalid => TypedResults.ValidationProblem(changed.Errors!),
            _ => TypedResults.NotFound(),
        };
    }

    private static async Task<Results<NoContent, NotFound>> Remove(
        int id,
        CurrentAccess current,
        ConnectionService connections,
        CancellationToken cancellationToken
    )
    {
        var state = await current.GetAsync();
        var connection = await connections.FindAsync(id, cancellationToken);
        if (connection is null || !ConnectionManagers.MayRemove(state, connection))
            return TypedResults.NotFound();

        return await connections.RemoveAsync(id, state.User?.Id, cancellationToken) == AccessStatus.Ok
            ? TypedResults.NoContent()
            : TypedResults.NotFound();
    }

    private static async Task<Results<Ok<ConnectionInfo>, ValidationProblem, NotFound>> SetLibraries(
        int id,
        ConnectionLibrariesInput input,
        CurrentAccess current,
        ConnectionSharing sharing,
        CancellationToken cancellationToken
    )
    {
        var state = await current.GetAsync();
        var result = await sharing.SetTenantsAsync(
            id,
            input.AllLibraries,
            input.LibraryIds ?? [],
            state.User?.Id,
            cancellationToken
        );
        return Answer(result);
    }

    private static async Task<Results<Ok<ConnectionInfo>, ValidationProblem, NotFound>> SetGroups(
        int id,
        ConnectionGroupsInput input,
        CurrentAccess current,
        ConnectionSharing sharing,
        CancellationToken cancellationToken
    )
    {
        var state = await current.GetAsync();
        if (state.Tenant is not { } tenant)
            return TypedResults.NotFound();

        var result = await sharing.SetGroupsAsync(
            id,
            tenant.Id,
            input.Groups ?? new Dictionary<int, FolderLevel>(),
            state.User?.Id,
            cancellationToken
        );
        return Answer(result);
    }

    private static async Task<IReadOnlyList<NamedItem>> GroupsAsync(
        AccessState state,
        AccessService accounts,
        CancellationToken cancellationToken
    ) =>
        state.Tenant is { } tenant && state.Has(Rights.LibraryConnections)
            ?
            [
                .. (await accounts.GetGroupsAsync(tenant.Id, cancellationToken))
                    .OrderBy(g => g.Name, StringComparer.CurrentCultureIgnoreCase)
                    .Select(g => new NamedItem(g.Id, g.Name)),
            ]
            : [];

    /// <summary>Who asks to use connections (<see cref="ConnectionAccess"/>).</summary>
    public static ConnectionSubject SubjectOf(AccessState state) =>
        new(state.TenantId, state.User?.Id, state.User?.IsServerAdmin == true, state.User is null or { IsGuest: true });

    private static Results<Ok<ConnectionInfo>, ValidationProblem, NotFound> Answer(
        AccessResult<ConnectionInfo> result
    ) =>
        result.Status switch
        {
            AccessStatus.Ok => TypedResults.Ok(result.Value!),
            AccessStatus.Invalid => TypedResults.ValidationProblem(result.Errors!),
            _ => TypedResults.NotFound(),
        };
}
