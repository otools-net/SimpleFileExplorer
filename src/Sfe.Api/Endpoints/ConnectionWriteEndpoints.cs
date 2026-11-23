using Otools.Net.Sfe.Api.Security;
using Otools.Net.Sfe.Core.Access;
using Otools.Net.Sfe.Core.Connections;
using Otools.Net.Sfe.Core.Files;

namespace Otools.Net.Sfe.Api.Endpoints;

/// <summary>
/// A connection's files changed: new folder, rename, delete, move and copy, done by the provider at once (no job).
/// Paths go through <see cref="RemotePaths.Normalize"/>; a connection the session may not use answers 404, one it
/// only reads 403; the route's <c>files.*</c> right is asked as in the library, and at each path by the
/// connection's folder rules before the provider is called (<see cref="ConnectionWriteChecks"/>). Each change drops
/// the connection's cached listings (<see cref="RemoteListings.Forget"/>), so the next page shows it.
/// </summary>
public static class ConnectionWriteEndpoints
{
    /// <summary>Items deleted, moved or copied in one request.</summary>
    public const int MaxItems = 1000;

    private const string Scope = "; 404 for a connection the subject may not use, 403 for one it only reads";

    private static readonly string NameDescription = $"""
        400 with `errors.name` for a name no provider takes (or this one refuses), 404 when the folder is not
        there, 409 `{FileEndpoints.ExistsType}` when something of that name is. A renamed folder's rules follow it
        for every library; 409 `{FileEndpoints.FolderRulesType}` when the library's rules are set where it goes.
        """;

    private static readonly string DeleteDescription = $"""
        Deletes each of `paths` (at most {MaxItems}) into the provider's own trash when it has one; `permanent` is
        ignored. Each item's `status` says what became of it (`trashId` is always null).
        """;

    private static readonly string TransferDescription = $"""
        `kind` `move` or `copy`, each of `paths` (at most {MaxItems}) into the folder `target`, done by the provider.
        `conflict`: `fail` (status `exists`), `keepBoth` (the provider's numbering) or `replace` (a file only; a
        folder is never replaced). An item moved where it is is `skipped`; a folder into itself `invalid`. 409
        `{ConnectionProblems.NotSupportedType}` when the provider cannot move or copy on its side. A moved folder's
        rules follow it for every library (`conflict` status when the library's are set where it goes); a move
        letting more of the library's people see or change a folder answers 409 `{ConnectionMoves.AccessGainsType}`
        with its `gains` (as `api/files/jobs/check`) unless `keepAccess` is given (true pins their current access).
        """;

    public static RouteGroupBuilder MapConnectionWriteEndpoints(this RouteGroupBuilder api)
    {
        var files = api.MapGroup("/connections/{id:int}/files").WithTags("Connections");

        files
            .MapPost("/folders", CreateFolder)
            .RequireRateLimiting(RequestLimits.Connections)
            .RequireRight(Rights.UseConnections)
            .FolderScoped("files.createFolders" + Scope)
            .WithName("CreateConnectionFolder")
            .WithSummary("Creates a folder in a connection.")
            .WithDescription(NameDescription);
        files
            .MapPost("/rename", Rename)
            .RequireRateLimiting(RequestLimits.Connections)
            .RequireRight(Rights.UseConnections)
            .FolderScoped("files.rename" + Scope)
            .WithName("RenameConnectionItem")
            .WithSummary("Renames a file or folder of a connection, in its folder.")
            .WithDescription(NameDescription);
        files
            .MapPost("/delete", Delete)
            .RequireRateLimiting(RequestLimits.Connections)
            .RequireRight(Rights.UseConnections)
            .FolderScoped("files.delete" + Scope)
            .WithName("DeleteConnectionItems")
            .WithSummary("Deletes files and folders of a connection.")
            .WithDescription(DeleteDescription);
        files
            .MapPost("/transfer", Transfer)
            .RequireRateLimiting(RequestLimits.Connections)
            .RequireRight(Rights.UseConnections)
            .FolderScoped("files.move or files.copy" + Scope)
            .WithName("TransferConnectionItems")
            .WithSummary("Moves or copies files and folders of a connection into one of its folders.")
            .WithDescription(TransferDescription);
        return api;
    }

    private static async Task<IResult> CreateFolder(
        int id,
        CreateFolderRequest request,
        [AsParameters] ConnectionFileServices files,
        [AsParameters] ConnectionWriteServices writes,
        CancellationToken cancellationToken
    )
    {
        var (open, answer) = await files.OpenAsync(id, Rights.CreateFolders, write: true, cancellationToken);
        if (open is null)
            return answer!;
        if (RemotePaths.Normalize(request.Path) is not { } folder)
            return TypedResults.NotFound();
        if (!RemotePaths.IsValidName(request.Name))
            return InvalidName("The name cannot be used.");
        var path = RemotePaths.Combine(folder, request.Name!);
        if (ConnectionWriteChecks.NewItem(open, folder, path, Rights.CreateFolders) is { } refused)
            return refused;

        return await files.RunAsync(open, async storage =>
        {
            if (folder.Length > 0 && await storage.GetAsync(folder, cancellationToken) is not { IsFolder: true })
                return TypedResults.NotFound();
            return await ChangeAsync(id, writes, async () =>
                await ReportedAsync(open, writes, await storage.CreateFolderAsync(path, cancellationToken))
            );
        });
    }

    private static async Task<IResult> Rename(
        int id,
        RenameRequest request,
        [AsParameters] ConnectionFileServices files,
        [AsParameters] ConnectionWriteServices writes,
        CancellationToken cancellationToken
    )
    {
        var (open, answer) = await files.OpenAsync(id, Rights.RenameFiles, write: true, cancellationToken);
        if (open is null)
            return answer!;
        if (RemotePaths.Normalize(request.Path) is not { Length: > 0 } path)
            return TypedResults.NotFound();
        if (!RemotePaths.IsValidName(request.Name))
            return InvalidName("The name cannot be used.");

        var to = RemotePaths.Combine(RemotePaths.Parent(path), request.Name!);
        if (OpenConnection.AnswerOf(open.CheckThroughout(path, Rights.RenameFiles)) is { } refused)
            return refused;
        if (
            to != path
            && ConnectionWriteChecks.NewItem(open, RemotePaths.Parent(to), to, Rights.RenameFiles) is { } taken
        )
        {
            return taken;
        }

        return await files.RunAsync(open, async storage =>
        {
            if (to == path)
            {
                return await storage.GetAsync(path, cancellationToken) is { } same
                    ? TypedResults.Ok(ConnectionListings.ToItem(same, writes.Classifier))
                    : TypedResults.NotFound();
            }
            // In its folder: nobody gains access (rules where it goes are refused), so nothing is asked.
            var moves = await ConnectionMoves.CreateAsync(open, writes, files.Current, false, cancellationToken);
            return await AnswerAsync(id, writes, async () =>
            {
                var (item, refused) = await moves.MoveAsync(
                    path,
                    to,
                    () => storage.MoveAsync(path, to, RemoteConflict.Fail, cancellationToken),
                    cancellationToken
                );
                if (item is null)
                    return ConnectionMoves.AnswerOf(refused!);
                await ReportAsync(writes, ConnectionItemChange.Moved(id, open.Capabilities, path, item));
                return TypedResults.Ok(ConnectionListings.ToItem(item, writes.Classifier));
            });
        });
    }

    private static async Task<IResult> Delete(
        int id,
        DeleteRequest request,
        [AsParameters] ConnectionFileServices files,
        [AsParameters] ConnectionWriteServices writes,
        CancellationToken cancellationToken
    )
    {
        var (open, answer) = await files.OpenAsync(id, Rights.DeleteFiles, write: true, cancellationToken);
        if (open is null)
            return answer!;

        return await files.RunAsync(open, async storage =>
        {
            var results = new List<DeletedItem>();
            try
            {
                foreach (var asked in (request.Paths ?? []).Distinct(StringComparer.Ordinal).Take(MaxItems))
                    results.Add(await DeleteOneAsync((open, writes), asked, cancellationToken));
            }
            finally
            {
                writes.Listings.Forget(id);
            }
            return TypedResults.Ok(new DeleteResults(results, Permanent: false));
        });
    }

    private static async Task<DeletedItem> DeleteOneAsync(
        (OpenConnection Open, ConnectionWriteServices Writes) on,
        string asked,
        CancellationToken cancellationToken
    )
    {
        var open = on.Open;
        if (RemotePaths.Normalize(asked) is not { Length: > 0 } path)
            return new DeletedItem(asked, FileOperationStatus.NotFound);
        var verdict = open.CheckThroughout(path, Rights.DeleteFiles);
        if (ConnectionWriteChecks.StatusOf(verdict) is { } refused)
            return new DeletedItem(asked, refused);
        try
        {
            await open.Storage.DeleteAsync(path, cancellationToken);
            var id = open.Connection.Id;
            await ReportAsync(on.Writes, ConnectionItemChange.Removed(id, open.Capabilities, path));
            return new DeletedItem(asked, FileOperationStatus.Done);
        }
        catch (RemoteStorageException error) when (StatusOf(error.Kind) is { } status)
        {
            return new DeletedItem(asked, status, error.Message);
        }
    }

    private static async Task<IResult> Transfer(
        int id,
        ConnectionTransferRequest request,
        [AsParameters] ConnectionFileServices files,
        [AsParameters] ConnectionWriteServices writes,
        CancellationToken cancellationToken
    )
    {
        bool? move = request.Kind switch
        {
            "move" => true,
            "copy" => false,
            _ => null,
        };
        if (move is null)
            return Invalid("kind", "Move or copy.");
        if (ConnectionWriteChecks.ConflictOf(request.Conflict) is not { } conflict)
            return Invalid("conflict", "Fail, keepBoth or replace.");
        var right = move.Value ? Rights.MoveFiles : Rights.CopyFiles;
        var (open, answer) = await files.OpenAsync(id, right, write: true, cancellationToken);
        if (open is null)
            return answer!;
        if (!(move.Value ? open.Capabilities.ServerSideMove : open.Capabilities.ServerSideCopy))
            return NotSupported(move.Value);
        if (RemotePaths.Normalize(request.Target) is not { } target)
            return TypedResults.NotFound();
        if (OpenConnection.AnswerOf(open.Check(target, right)) is { } refused)
            return refused;

        return await files.RunAsync(open, async storage =>
        {
            if (target.Length > 0 && await storage.GetAsync(target, cancellationToken) is not { IsFolder: true })
                return TypedResults.NotFound();
            var moves = await ConnectionMoves.CreateAsync(
                open,
                writes,
                files.Current,
                request.KeepAccess,
                cancellationToken
            );
            var step = new ConnectionTransferStep(open, target, conflict, move.Value, writes.Classifier, moves);
            var paths = (request.Paths ?? []).Distinct(StringComparer.Ordinal).Take(MaxItems).ToList();
            if (move.Value && await moves.AskAsync(Movable(step, paths), cancellationToken) is { } ask)
                return ask;
            var results = new List<ConnectionTransferItem>();
            try
            {
                foreach (var asked in paths)
                    results.Add(await TransferOneAsync(step, asked, writes.Changes, cancellationToken));
            }
            finally
            {
                writes.Listings.Forget(id);
            }
            return TypedResults.Ok(new ConnectionTransferResults(results));
        });
    }

    private static async Task<ConnectionTransferItem> TransferOneAsync(
        ConnectionTransferStep step,
        string asked,
        ConnectionChanges changes,
        CancellationToken cancellationToken
    )
    {
        if (RemotePaths.Normalize(asked) is not { Length: > 0 } path)
            return new(asked, FileOperationStatus.NotFound);
        var to = RemotePaths.Combine(step.Target, RemotePaths.NameOf(path));
        if (ItemStatus(step, path, to) is { } stopped)
            return stopped with { Path = asked };

        var (storage, id, capabilities) = (step.Open.Storage, step.Open.Connection.Id, step.Open.Capabilities);
        try
        {
            if (!step.Move)
            {
                var copy = await storage.CopyAsync(path, to, step.Conflict, cancellationToken);
                await changes.ReportAsync(ConnectionItemChange.Copied(id, capabilities, copy), CancellationToken.None);
                return new(asked, FileOperationStatus.Done, Item: ConnectionListings.ToItem(copy, step.Classifier));
            }
            var (place, conflict) = await step.Moves.PlaceAsync(path, to, step.Conflict, cancellationToken);
            var (item, refused) = await step.Moves.MoveAsync(
                path,
                place,
                () => storage.MoveAsync(path, place, conflict, cancellationToken),
                cancellationToken
            );
            if (item is null)
                return new(asked, ConnectionMoves.StatusOf(refused!), refused!.Problem);
            await changes.ReportAsync(ConnectionItemChange.Moved(id, capabilities, path, item), CancellationToken.None);
            return new(asked, FileOperationStatus.Done, Item: ConnectionListings.ToItem(item, step.Classifier));
        }
        catch (RemoteStorageException error) when (StatusOf(error.Kind) is { } status)
        {
            return new(asked, status, error.Message);
        }
    }

    /// <summary>
    /// Tells the connection's indexes and open listings what changed (<see cref="ConnectionChanges"/>): the change
    /// is made at the provider, so a request cut short still writes it.
    /// </summary>
    private static Task ReportAsync(ConnectionWriteServices writes, ConnectionItemChange change) =>
        writes.Changes.ReportAsync(change, CancellationToken.None);

    /// <summary>A new item, told (<see cref="ReportAsync"/>), as its answer.</summary>
    private static async Task<RemoteItem> ReportedAsync(
        OpenConnection open,
        ConnectionWriteServices writes,
        RemoteItem made
    )
    {
        await ReportAsync(writes, ConnectionItemChange.Put(open.Connection.Id, open.Capabilities, made));
        return made;
    }

    /// <summary>
    /// A change answered with the item it made; the provider's refusal of the name as the route's own 400.
    /// </summary>
    private static Task<IResult> ChangeAsync(int id, ConnectionWriteServices writes, Func<Task<RemoteItem>> change) =>
        AnswerAsync(id, writes, async () =>
            TypedResults.Ok(ConnectionListings.ToItem(await change(), writes.Classifier))
        );

    /// <inheritdoc cref="ChangeAsync"/>
    private static async Task<IResult> AnswerAsync(int id, ConnectionWriteServices writes, Func<Task<IResult>> change)
    {
        try
        {
            return await change();
        }
        catch (RemoteStorageException error) when (error.Kind == RemoteErrorKind.InvalidName)
        {
            return InvalidName(error.Message);
        }
        finally
        {
            writes.Listings.Forget(id);
        }
    }

    /// <summary>
    /// What stops one item of a move or copy before the provider is called (its place, the target, a move onto
    /// itself or into itself); null when nothing does.
    /// </summary>
    private static ConnectionTransferItem? ItemStatus(ConnectionTransferStep step, string path, string to)
    {
        var comparison = step.Open.Folders.Comparison;
        if (ConnectionWriteChecks.SourceStatus(step.Open, path, step.Move) is { } refused)
            return new(path, refused);
        if (step.Move && to.Equals(path, comparison))
            return new(path, FileOperationStatus.Skipped);
        if (RemotePaths.IsInTree(step.Target, path, comparison))
            return new(path, FileOperationStatus.Invalid, "A folder cannot go into itself.");
        var right = step.Move ? Rights.MoveFiles : Rights.CopyFiles;
        return ConnectionWriteChecks.TargetStatus(step.Open, to, right) is { } held ? new(path, held) : null;
    }

    /// <summary>The items of a move that would be moved (checked as each one is), with where they go.</summary>
    private static IEnumerable<(string From, string To)> Movable(ConnectionTransferStep step, List<string> paths) =>
        paths
            .Select(RemotePaths.Normalize)
            .OfType<string>()
            .Where(path => path.Length > 0)
            .Select(path => (From: path, To: RemotePaths.Combine(step.Target, RemotePaths.NameOf(path))))
            .Where(move => ItemStatus(step, move.From, move.To) is null);

    private static FileOperationStatus? StatusOf(RemoteErrorKind kind) => ConnectionWriteChecks.StatusOf(kind);

    private static IResult InvalidName(string message) => Invalid("name", message);

    private static IResult Invalid(string field, string message) =>
        TypedResults.ValidationProblem(new Dictionary<string, string[]> { [field] = [message] });

    private static IResult NotSupported(bool move) =>
        TypedResults.Problem(
            move
                ? "This connection can't move files within itself."
                : "This connection can't copy files within itself.",
            statusCode: StatusCodes.Status409Conflict,
            type: ConnectionProblems.NotSupportedType
        );
}
