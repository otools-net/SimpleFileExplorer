using Sfe.Api.Security;
using Sfe.Core.Access;
using Sfe.Core.Connections;
using Sfe.Core.Files;

namespace Sfe.Api.Endpoints;

/// <summary>
/// A connection's files changed: new folder, rename, delete, move and copy, done by the provider at once (no job).
/// Paths go through <see cref="RemotePaths.Normalize"/>; a connection the session may not use answers 404, one it
/// only reads 403; the route's <c>files.*</c> right is asked as in the library. Each change drops the connection's
/// cached listings (<see cref="RemoteListings.Forget"/>), so the next page shows it.
/// </summary>
public static class ConnectionWriteEndpoints
{
    /// <summary>Items deleted, moved or copied in one request.</summary>
    public const int MaxItems = 1000;

    private const string Scope = "; 404 for a connection the subject may not use, 403 for one it only reads";

    private static readonly string NameDescription = $"""
        400 with `errors.name` for a name no provider takes (or this one refuses), 404 when the folder is not
        there, 409 `{FileEndpoints.ExistsType}` when something of that name is.
        """;

    private static readonly string DeleteDescription = $"""
        Deletes each of `paths` (at most {MaxItems}) into the provider's own trash when it has one; `permanent` is
        ignored. Each item's `status` says what became of it (`trashId` is always null).
        """;

    private static readonly string TransferDescription = $"""
        `kind` `move` or `copy`, each of `paths` (at most {MaxItems}) into the folder `target`, done by the provider.
        `conflict`: `fail` (status `exists`), `keepBoth` (the provider's numbering) or `replace` (a file only; a
        folder is never replaced). An item moved where it is is `skipped`; a folder into itself `invalid`. 409
        `{ConnectionProblems.NotSupportedType}` when the provider cannot move or copy on its side.
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

        return await files.RunAsync(open, async storage =>
        {
            if (folder.Length > 0 && await storage.GetAsync(folder, cancellationToken) is not { IsFolder: true })
                return TypedResults.NotFound();
            var path = RemotePaths.Combine(folder, request.Name!);
            return await ChangeAsync(id, writes, () => storage.CreateFolderAsync(path, cancellationToken));
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
        return await files.RunAsync(open, async storage =>
        {
            if (to == path)
            {
                return await storage.GetAsync(path, cancellationToken) is { } same
                    ? TypedResults.Ok(ConnectionListings.ToItem(same, writes.Classifier))
                    : TypedResults.NotFound();
            }
            return await ChangeAsync(id, writes, () =>
                storage.MoveAsync(path, to, RemoteConflict.Fail, cancellationToken)
            );
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
                    results.Add(await DeleteOneAsync(storage, asked, cancellationToken));
            }
            finally
            {
                writes.Listings.Forget(id);
            }
            return TypedResults.Ok(new DeleteResults(results, Permanent: false));
        });
    }

    private static async Task<DeletedItem> DeleteOneAsync(
        IRemoteStorage storage,
        string asked,
        CancellationToken cancellationToken
    )
    {
        if (RemotePaths.Normalize(asked) is not { Length: > 0 } path)
            return new DeletedItem(asked, FileOperationStatus.NotFound);
        try
        {
            await storage.DeleteAsync(path, cancellationToken);
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
        if (ConflictOf(request.Conflict) is not { } conflict)
            return Invalid("conflict", "Fail, keepBoth or replace.");
        var right = move.Value ? Rights.MoveFiles : Rights.CopyFiles;
        var (open, answer) = await files.OpenAsync(id, right, write: true, cancellationToken);
        if (open is null)
            return answer!;
        if (!(move.Value ? open.Capabilities.ServerSideMove : open.Capabilities.ServerSideCopy))
            return NotSupported(move.Value);
        if (RemotePaths.Normalize(request.Target) is not { } target)
            return TypedResults.NotFound();

        var comparison = RemotePaths.ComparisonOf(open.Capabilities);
        return await files.RunAsync(open, async storage =>
        {
            if (target.Length > 0 && await storage.GetAsync(target, cancellationToken) is not { IsFolder: true })
                return TypedResults.NotFound();
            var step = new ConnectionTransferStep(storage, target, conflict, move.Value, comparison, writes.Classifier);
            var results = new List<ConnectionTransferItem>();
            try
            {
                foreach (var asked in (request.Paths ?? []).Distinct(StringComparer.Ordinal).Take(MaxItems))
                    results.Add(await TransferOneAsync(step, asked, cancellationToken));
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
        CancellationToken cancellationToken
    )
    {
        if (RemotePaths.Normalize(asked) is not { Length: > 0 } path)
            return new(asked, FileOperationStatus.NotFound);
        var to = RemotePaths.Combine(step.Target, RemotePaths.NameOf(path));
        if (step.Move && to.Equals(path, step.Comparison))
            return new(asked, FileOperationStatus.Skipped);
        if (RemotePaths.IsInTree(step.Target, path, step.Comparison))
            return new(asked, FileOperationStatus.Invalid, "A folder cannot go into itself.");
        try
        {
            var item = step.Move
                ? await step.Storage.MoveAsync(path, to, step.Conflict, cancellationToken)
                : await step.Storage.CopyAsync(path, to, step.Conflict, cancellationToken);
            return new(asked, FileOperationStatus.Done, Item: ConnectionListings.ToItem(item, step.Classifier));
        }
        catch (RemoteStorageException error) when (StatusOf(error.Kind) is { } status)
        {
            return new(asked, status, error.Message);
        }
    }

    /// <summary>
    /// A change answered with the item it made; the provider's refusal of the name as the route's own 400.
    /// </summary>
    private static async Task<IResult> ChangeAsync(
        int id,
        ConnectionWriteServices writes,
        Func<Task<RemoteItem>> change
    )
    {
        try
        {
            return TypedResults.Ok(ConnectionListings.ToItem(await change(), writes.Classifier));
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
    /// An item's status for what the provider refused about it; null for what stops the whole request (the
    /// sign-in ended, a wait, the provider unreachable), answered by <see cref="ConnectionFileServices.RunAsync"/>.
    /// </summary>
    private static FileOperationStatus? StatusOf(RemoteErrorKind kind) =>
        kind switch
        {
            RemoteErrorKind.NotFound => FileOperationStatus.NotFound,
            RemoteErrorKind.Exists => FileOperationStatus.Exists,
            RemoteErrorKind.InvalidName => FileOperationStatus.Invalid,
            RemoteErrorKind.Refused => FileOperationStatus.Forbidden,
            RemoteErrorKind.TooLarge or RemoteErrorKind.NotSupported => FileOperationStatus.Failed,
            _ => null,
        };

    /// <summary>The conflict choice as written (<c>fail</c> when none); null for another word.</summary>
    private static RemoteConflict? ConflictOf(string? value) =>
        value switch
        {
            null or "fail" => RemoteConflict.Fail,
            "keepBoth" => RemoteConflict.KeepBoth,
            "replace" => RemoteConflict.Replace,
            _ => null,
        };

    private static IResult InvalidName(string message) => Invalid("name", message);

    private static IResult Invalid(string field, string message) =>
        TypedResults.ValidationProblem(new Dictionary<string, string[]> { [field] = [message] });

    private static IResult NotSupported(bool move) =>
        TypedResults.Problem(
            move ? "The provider cannot move files on its side." : "The provider cannot copy files on its side.",
            statusCode: StatusCodes.Status409Conflict,
            type: ConnectionProblems.NotSupportedType
        );
}
