using System.Collections.Concurrent;
using Sfe.Api.Security;
using Sfe.Core.Access;
using Sfe.Core.Connections;
using Sfe.Core.Files;
using Sfe.Core.Transfers;

namespace Sfe.Api.Endpoints;

/// <summary>
/// Moves and copies between the library and a connection, or between two connections, as background jobs
/// (agents/docs/development/connections-plan.md, phase 5). They are the library's file jobs: followed, answered and
/// cancelled through <c>api/files/jobs</c>.
/// </summary>
public static class TransferEndpoints
{
    private static readonly string Description = $"""
        `kind` `move` or `copy`; `from`: a `connection` (null: the library) and its `paths` (at most
        {TransferJobs.MaxItems}); `to`: a `connection` (null: the library) and the folder `path`. Within the library
        use `api/files/jobs`, within one connection `api/connections/{"{id}"}/files/transfer`: 400 here. A connection
        the caller may not use answers 404, one it only reads 403 when written to (the target, a move's source), 409
        when it cannot be opened (sign in again). Answers 202 with the job, followed through `api/files/jobs/{"{id}"}`;
        `fromConnection` and `toConnection` name its sides. Files are verified (size, and the content hash when a side
        gives one) before a move takes its source away: into SFE's trash in the library, the provider's in a
        connection.
        """;

    public static RouteGroupBuilder MapTransferEndpoints(this RouteGroupBuilder api)
    {
        api.MapPost("/files/jobs/transfer", StartTransfer)
            .WithTags("Files")
            .RequireRateLimiting(RequestLimits.Connections)
            .RequireRight(Rights.MoveFiles, Rights.CopyFiles)
            .FolderScoped(
                "files.move or files.copy as in api/files/jobs on the library's side (checked again at each item); "
                    + "connections.use and the connection's access on a connection's side"
            )
            .WithName("StartTransfer")
            .WithSummary("Moves or copies files and folders between the library and a connection, in the background.")
            .WithDescription(Description);
        return api;
    }

    private static async Task<IResult> StartTransfer(
        TransferRequest request,
        [AsParameters] TransferAccessServices files,
        [AsParameters] ConnectionFileServices connections,
        [AsParameters] TransferServices transfers,
        CancellationToken cancellationToken
    )
    {
        var state = await connections.Current.GetAsync();
        if (state.Tenant is not { } tenant)
            return TypedResults.Forbid();
        if (Check(request) is { } invalid)
            return invalid;
        var move = request.Kind == "move";
        var right = move ? Rights.MoveFiles : Rights.CopyFiles;
        var connected = request.From!.Connection is not null || request.To!.Connection is not null;
        if (!state.Has(right) || connected && !state.Has(Rights.UseConnections))
            return TypedResults.Forbid();

        var sides = new TransferOpening(connections, transfers, right, cancellationToken);
        if (await sides.CheckAsync(request.From.Connection, write: move) is { } fromAnswer)
            return fromAnswer;
        if (await sides.CheckAsync(request.To!.Connection, write: true) is { } toAnswer)
            return toAnswer;

        var access = new FileJobAccess(
            files.Accounts,
            files.Rights,
            files.Paths,
            files.ServerFiles,
            files.Work,
            tenant,
            state.User?.Id
        );
        return await StartAsync(request, sides, (FileEndpoints.Actor(state), access, move), transfers.Jobs);
    }

    /// <summary>The job's own sides opened (for its lifetime), then the job started.</summary>
    private static async Task<IResult> StartAsync(
        TransferRequest request,
        TransferOpening sides,
        (FileActor Actor, IFileJobAccess Access, bool Move) who,
        TransferJobs jobs
    )
    {
        var partials = new ConcurrentBag<string>();
        var (source, sourceAnswer) = await sides.OpenAsync(request.From!.Connection, who.Actor, who.Access, partials);
        if (source is null)
            return sourceAnswer!;
        var (target, targetAnswer) = await sides.OpenAsync(request.To!.Connection, who.Actor, who.Access, partials);
        if (target is null)
        {
            await source.DisposeAsync();
            return targetAnswer!;
        }

        var view = jobs.Start(
            new TransferStart
            {
                Kind = who.Move ? FileJobKind.Move : FileJobKind.Copy,
                FromConnection = request.From.Connection,
                Paths = request.From.Paths!,
                To = new TransferPlace(request.To.Connection, request.To.Path ?? ""),
                Conflicts = request.Conflicts,
                Source = source,
                Target = target,
                Actor = who.Actor,
                Access = who.Access,
                Partials = partials,
                SignInEnded = sides.SignInEnded,
            }
        );
        return TypedResults.Accepted(ApiRoutes.Link($"files/jobs/{view.Id}"), view);
    }

    /// <summary>What is wrong with the request's words, as a 400; null when they are fine.</summary>
    private static IResult? Check(TransferRequest request)
    {
        if (request.Kind is not ("move" or "copy"))
            return Invalid("kind", "Move or copy.");
        if (request.From?.Paths is not { Count: > 0 and <= TransferJobs.MaxItems })
            return Invalid("from.paths", $"Name 1 to {TransferJobs.MaxItems} files or folders.");
        if (request.To is null)
            return Invalid("to", "Name the folder they go into.");
        if (request.From.Connection == request.To.Connection)
        {
            return Invalid(
                "to",
                "Within the library use api/files/jobs; within a connection, api/connections/{id}/files/transfer."
            );
        }
        return null;
    }

    private static IResult Invalid(string field, string message) =>
        TypedResults.ValidationProblem(new Dictionary<string, string[]> { [field] = [message] });
}
