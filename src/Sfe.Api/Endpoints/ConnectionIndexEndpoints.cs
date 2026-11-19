using Microsoft.AspNetCore.Http.HttpResults;
using Otools.Net.Sfe.Api.Security;
using Otools.Net.Sfe.Core.Connections;
using Otools.Net.Sfe.Core.Connections.Indexing;
using Otools.Net.Sfe.Core.Tasks;
using ConnectionInfo = Otools.Net.Sfe.Core.Connections.ConnectionInfo;

namespace Otools.Net.Sfe.Api.Endpoints;

/// <summary>
/// The index of a connection's files in the request's library (agents/docs/features/connection-index.md):
/// where its sync stands, and "Sync now". For who manages the connection (<see cref="ConnectionManagers"/>), and only
/// where the library uses it; no route gives the index's rows yet (folder rules come first).
/// </summary>
public static class ConnectionIndexEndpoints
{
    public static RouteGroupBuilder MapConnectionIndexEndpoints(this RouteGroupBuilder api)
    {
        var index = api.MapGroup("/connections/{id:int}/index").WithTags("Connections");

        index
            .MapGet("/", Status)
            .RequireRight(ConnectionManagers.Rights)
            .WithName("GetConnectionIndex")
            .WithSummary(
                "Where the index of a connection's files stands in the request's library: files and folders, last "
                    + "sync, a full listing under way, what stopped the last sync, what the running one does."
            )
            .WithDescription(
                "For who manages the connection (404 otherwise, and for a server's connection not offered to the "
                    + "library). Nothing is indexed before the connection is first opened in the library."
            );

        index
            .MapPost("/sync", Sync)
            .RequireRight(ConnectionManagers.Rights)
            .WithName("SyncConnectionIndex")
            .WithSummary("Syncs the index of a connection's files in the request's library now (in the background).")
            .WithDescription(
                "202 with the status (`running` already true when a sync was running: none is added). 409 "
                    + $"`{ConnectionProblems.UnusableType}` for a connection that is off. Each sync is a run of the "
                    + "\"Connection index\" task (history and log)."
            );

        return api;
    }

    private static async Task<Results<Ok<ConnectionIndexStatus>, NotFound>> Status(
        int id,
        CurrentAccess current,
        ConnectionService connections,
        ConnectionIndexes indexes,
        CancellationToken cancellationToken
    )
    {
        if (await ManagedHereAsync(id, current, connections, cancellationToken) is null)
            return TypedResults.NotFound();
        return TypedResults.Ok(await indexes.StatusAsync(id, cancellationToken));
    }

    private static async Task<Results<Accepted<ConnectionIndexStatus>, ProblemHttpResult, NotFound>> Sync(
        int id,
        CurrentAccess current,
        ConnectionService connections,
        ConnectionIndexes indexes,
        CancellationToken cancellationToken
    )
    {
        if (await ManagedHereAsync(id, current, connections, cancellationToken) is not { } connection)
            return TypedResults.NotFound();
        if (connection.Disabled)
            return TypedResults.Problem(
                ConnectionService.UnusableProblem,
                statusCode: StatusCodes.Status409Conflict,
                type: ConnectionProblems.UnusableType
            );

        await indexes.StartAsync(id, TaskTrigger.Manual, null);
        var status = await indexes.StatusAsync(id, cancellationToken);
        return TypedResults.Accepted(ApiRoutes.Location($"connections/{id}/index"), status);
    }

    /// <summary>The connection, when the session manages it and the request's library uses it.</summary>
    private static async Task<ConnectionInfo?> ManagedHereAsync(
        int id,
        CurrentAccess current,
        ConnectionService connections,
        CancellationToken cancellationToken
    )
    {
        var state = await current.GetAsync();
        var connection = await connections.FindAsync(id, cancellationToken);
        if (connection is null || !ConnectionManagers.MayManage(state, connection))
            return null;
        var here = state.TenantId;
        var used =
            connection.Level != ConnectionLevel.Server || connection.AllTenants || connection.TenantIds.Contains(here);
        return used ? connection : null;
    }
}
