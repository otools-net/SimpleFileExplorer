using System.Collections.Concurrent;
using Sfe.Api.Security;
using Sfe.Core.Access;
using Sfe.Core.Files;
using Sfe.Core.Transfers;

namespace Sfe.Api.Endpoints;

/// <summary>
/// Moves and copies between the library and a connection, or between two connections, as background jobs
/// (agents/docs/features/transfers.md). They are the library's file jobs: followed, answered and
/// cancelled through <c>api/files/jobs</c>.
/// </summary>
public static class TransferEndpoints
{
    /// <summary>The problem type of a retry asked while the job still runs.</summary>
    public const string JobRunningType = "job-running";

    /// <summary>The problem type of a retry of a job with nothing left to start again.</summary>
    public const string NothingToRetryType = "nothing-to-retry";

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

    private static readonly string RetryDescription = $"""
        Starts again, as a new job, the items of an ended transfer that failed or were not reached, with the same
        places and conflict choice, for 24 hours after it ended (and while the server runs): a folder already at the
        target is gone into, a file there of the same size (and, for a move, content hash) counts as transferred, a
        file whose writing broke is written again from its start. Access is checked again as
        `api/files/jobs/transfer` checks it. Answers 202 with the new job (its own id, followed through
        `api/files/jobs/{"{id}"}`); 404 for a job that is not one of the
        caller's transfers; 409 `{JobRunningType}` while it runs, `{NothingToRetryType}` when nothing failed (or it was
        started again already, or is too old).
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
        api.MapPost("/files/jobs/{id:int}/retry", RetryTransfer)
            .WithTags("Files")
            .RequireRateLimiting(RequestLimits.Connections)
            .RequireRight(Rights.MoveFiles, Rights.CopyFiles)
            .FolderScoped("as api/files/jobs/transfer, for the job's kind (files.move or files.copy), checked again")
            .WithName("RetryTransfer")
            .WithSummary("Starts again the failed items of one of the caller's ended transfers.")
            .WithDescription(RetryDescription);
        return api;
    }

    private static Task<IResult> StartTransfer(
        TransferRequest request,
        [AsParameters] TransferAccessServices files,
        [AsParameters] ConnectionFileServices connections,
        [AsParameters] TransferServices transfers,
        CancellationToken cancellationToken
    ) => StartCheckedAsync((request, null), files, connections, transfers, cancellationToken);

    private static async Task<IResult> RetryTransfer(
        int id,
        [AsParameters] TransferAccessServices files,
        [AsParameters] ConnectionFileServices connections,
        [AsParameters] TransferServices transfers,
        CancellationToken cancellationToken
    )
    {
        var owner = (await connections.Current.GetAsync()).User?.Id;
        if (transfers.Jobs.FindRetry(id, owner) is not { } retry)
            return transfers.Jobs.Find(id, owner) is { } job ? NotRetried(job.State) : TypedResults.NotFound();
        var request = new TransferRequest(
            retry.Kind == FileJobKind.Move ? "move" : "copy",
            new TransferSide(retry.FromConnection, retry.Paths),
            new TransferTarget(retry.To.ConnectionId, retry.To.Path),
            retry.Conflicts
        );
        return await StartCheckedAsync((request, id), files, connections, transfers, cancellationToken);
    }

    /// <summary>The request checked (its words, the rights, both sides), then the job started.</summary>
    /// <param name="ask">The request, and the ended job whose failed items it starts again.</param>
    private static async Task<IResult> StartCheckedAsync(
        (TransferRequest Request, int? RetryOf) ask,
        TransferAccessServices files,
        ConnectionFileServices connections,
        TransferServices transfers,
        CancellationToken cancellationToken
    )
    {
        var request = ask.Request;
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

        var sides = new TransferOpening(connections, transfers, files, right, cancellationToken);
        if (await sides.CheckAsync(request.From.Connection, write: move) is { } fromAnswer)
            return fromAnswer;
        if (await sides.CheckAsync(request.To!.Connection, write: true, request.To.Path ?? "") is { } toAnswer)
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
        return await StartAsync(ask, sides, (FileEndpoints.Actor(state), access, move), transfers.Jobs);
    }

    /// <summary>The job's own sides opened (for its lifetime), then the job started.</summary>
    private static async Task<IResult> StartAsync(
        (TransferRequest Request, int? RetryOf) ask,
        TransferOpening sides,
        (FileActor Actor, IFileJobAccess Access, bool Move) who,
        TransferJobs jobs
    )
    {
        var request = ask.Request;
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

        var start = new TransferStart
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
            FromName = sides.NameOf(request.From.Connection),
            ToName = sides.NameOf(request.To.Connection),
        };
        var view = ask.RetryOf is { } ended ? jobs.Retry(ended, start) : jobs.Start(start);
        if (view is null)
        {
            await source.DisposeAsync();
            await target.DisposeAsync();
            return NotRetried(FileJobState.Completed);
        }
        return TypedResults.Accepted(ApiRoutes.Link($"files/jobs/{view.Id}"), view);
    }

    /// <summary>The 409 of a retry: the job still runs, or has nothing to start again.</summary>
    private static IResult NotRetried(FileJobState state) =>
        state is FileJobState.Running or FileJobState.Waiting
            ? TypedResults.Problem(
                "The job is still running: it can be started again once it ends.",
                statusCode: StatusCodes.Status409Conflict,
                type: JobRunningType
            )
            : TypedResults.Problem(
                "Nothing of this job is left to start again: its items went, or were started again already.",
                statusCode: StatusCodes.Status409Conflict,
                type: NothingToRetryType
            );

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
