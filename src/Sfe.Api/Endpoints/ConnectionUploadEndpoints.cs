using Microsoft.AspNetCore.Mvc;
using Sfe.Api.Security;
using Sfe.Core.Access;
using Sfe.Core.Connections;
using Sfe.Core.Files;

namespace Sfe.Api.Endpoints;

/// <summary>
/// Resumable uploads into a connection: the library's routes (<see cref="UploadEndpoints"/>) in paths, bodies and
/// answers, so the client only changes their base. Each chunk goes on to the provider's upload session at once (no
/// file on the server's disk); <see cref="RemoteUploads"/> keeps the records, and the provider's session id never
/// leaves the server. An upload is its owner's only: anyone else is answered 404.
/// </summary>
public static class ConnectionUploadEndpoints
{
    private const string UploadRoute = "/{uploadId:regex(^[0-9a-f]{{32}}$)}";

    private const string Scope = "; 404 for a connection the subject may not use, 403 for one it only reads";

    private static readonly string StartDescription = $"""
        Checks the folder and the name (a path below the folder for a folder upload, each part a name any provider
        takes) and opens an upload session at the provider; answers the upload (201) with its `id`. At most
        {RemoteUploads.MaxPerOwner} unfinished uploads per person (429), dropped after
        {RemoteUploads.IdleLimit.TotalHours:N0} hours without a chunk. 409 `{ConnectionProblems.NotSupportedType}`
        when the provider cannot take a file in parts.
        """;

    private static readonly string ChunkDescription = $"""
        The body goes on to the provider at once. `offset` must be the bytes the provider has: another one answers
        409 with the upload, to resend from there. A chunk passing the size, {Uploads.MaxChunkBytes / 1024 / 1024}
        MB or the provider's part limit is refused (400).
        """;

    private static readonly string FinishDescription = $"""
        `conflicts`: `ask` (409 `{FileEndpoints.ExistsType}` when the name is taken), `replace` (a file only, by
        the provider; needs `files.delete`), `skip` (the upload is dropped, 204), `keepBoth` (the provider's
        numbering). Folders on the way are made by the provider. Answers the new file as listings show it.
        """;

    public static RouteGroupBuilder MapConnectionUploadEndpoints(this RouteGroupBuilder api)
    {
        var uploads = api.MapGroup("/connections/{id:int}/files/uploads").WithTags("Connections");

        uploads
            .MapPost("/check", Check)
            .RequireRateLimiting(RequestLimits.Connections)
            .RequireRight(Rights.UseConnections)
            .FolderScoped("files.upload" + Scope)
            .WithName("CheckConnectionUploads")
            .WithSummary("Which of these names are already taken in a connection's folder.");
        uploads
            .MapPost("", Start)
            .RequireRateLimiting(RequestLimits.Connections)
            .RequireRight(Rights.UseConnections)
            .FolderScoped("files.upload" + Scope)
            .WithName("StartConnectionUpload")
            .WithSummary("Starts an upload into a connection.")
            .WithDescription(StartDescription);
        uploads
            .MapGet("", List)
            .RequireRateLimiting(RequestLimits.Connections)
            .RequireRight(Rights.UseConnections)
            .FolderScoped("files.upload; the caller's own uploads only")
            .WithName("ListConnectionUploads")
            .WithSummary("The caller's unfinished uploads into a connection.");
        uploads
            .MapGet(UploadRoute, Get)
            .RequireRateLimiting(RequestLimits.Connections)
            .RequireRight(Rights.UseConnections)
            .FolderScoped("files.upload; the caller's own upload only")
            .WithName("GetConnectionUpload")
            .WithSummary("One of the caller's unfinished uploads into a connection: how much the provider has.");
        MapChanges(uploads);
        return api;
    }

    private static void MapChanges(RouteGroupBuilder uploads)
    {
        uploads
            .MapPut(UploadRoute, Append)
            .RequireRateLimiting(RequestLimits.Chunks)
            .RequireRight(Rights.UseConnections)
            .FolderScoped("files.upload" + Scope)
            .WithMetadata(new RequestSizeLimitAttribute(Uploads.MaxChunkBytes + 1))
            .WithName("SendConnectionUploadChunk")
            .WithSummary("Passes a chunk (the body) on to the provider at `offset`.")
            .WithDescription(ChunkDescription);
        uploads
            .MapPost(UploadRoute + "/finish", Finish)
            .RequireRateLimiting(RequestLimits.Connections)
            .RequireRight(Rights.UseConnections)
            .FolderScoped("files.upload, files.delete to replace" + Scope)
            .WithName("FinishConnectionUpload")
            .WithSummary("Makes the file of a fully sent upload in its folder of the connection.")
            .WithDescription(FinishDescription);
        uploads
            .MapDelete(UploadRoute, Cancel)
            .RequireRateLimiting(RequestLimits.Connections)
            .RequireRight(Rights.UseConnections)
            .FolderScoped("files.upload; the caller's own upload only")
            .WithName("CancelConnectionUpload")
            .WithSummary("Cancels one of the caller's unfinished uploads into a connection.");
    }

    private static async Task<IResult> Check(
        int id,
        UploadCheckRequest request,
        [AsParameters] ConnectionFileServices files,
        CancellationToken cancellationToken
    )
    {
        var (open, answer) = await files.OpenAsync(id, Rights.UploadFiles, write: true, cancellationToken);
        if (open is null)
            return answer!;
        if (request.Names is not { Count: > 0 and <= UploadEndpoints.MaxCheckNames } names)
            return Invalid("names", $"Name 1 to {UploadEndpoints.MaxCheckNames} files.");
        if (RemotePaths.Normalize(request.Target) is not { } target)
            return TypedResults.NotFound();

        var canReplace = (await files.Current.GetAsync()).Has(Rights.DeleteFiles);
        return await files.RunAsync(open, async storage =>
        {
            if (!await IsFolderAsync(storage, target, cancellationToken))
                return TypedResults.NotFound();
            var taken = await ConnectionUploadNames.TakenAsync(
                storage,
                target,
                names,
                open.Capabilities,
                cancellationToken
            );
            return TypedResults.Ok(new UploadCheck(taken, canReplace));
        });
    }

    private static async Task<IResult> Start(
        int id,
        UploadStart request,
        [AsParameters] ConnectionFileServices files,
        [AsParameters] ConnectionUploadServices uploads,
        CancellationToken cancellationToken
    )
    {
        var (open, answer) = await files.OpenAsync(id, Rights.UploadFiles, write: true, cancellationToken);
        if (open is null)
            return answer!;
        if (!open.Capabilities.UploadsInParts)
            return NotSupported();
        if (RemotePaths.Normalize(request.Target) is not { } target)
            return TypedResults.NotFound();
        if (ConnectionUploadNames.PathOf(request.Name) is not { } name)
            return Invalid("name", "The name cannot be used.");
        if (request.Size < 0)
            return Invalid("name", "The size is not valid.");
        // The space's limit holds whatever the place (Files:Uploads:MaxFileSizeMegabytes).
        if (uploads.Options.CurrentValue.Files.Uploads.MaxFileSizeMegabytes is > 0 and var max)
        {
            if (request.Size > max * 1024L * 1024)
                return Invalid(
                    "name",
                    $"“{RemotePaths.NameOf(name)}” is larger than the {max:N0} MB a file may have here."
                );
        }
        var state = await files.Current.GetAsync();
        if (state.User is not { } user)
            return TypedResults.Forbid();
        if (uploads.Uploads.IsFull(user.Id))
            return TooMany();

        var start = new RemoteUploadStart
        {
            ConnectionId = id,
            TenantId = state.TenantId,
            OwnerId = user.Id,
            OwnerName = user.DisplayName ?? user.UserName,
            Target = target,
            Name = name,
            Size = request.Size,
            Modified = request.Modified,
        };
        return await files.RunAsync(open, storage => BeginAsync(storage, start, uploads.Uploads, cancellationToken));
    }

    private static async Task<IResult> BeginAsync(
        IRemoteStorage storage,
        RemoteUploadStart start,
        RemoteUploads uploads,
        CancellationToken cancellationToken
    )
    {
        if (!await IsFolderAsync(storage, start.Target, cancellationToken))
            return TypedResults.NotFound();
        var upload = uploads.Add(start, await storage.BeginUploadAsync(cancellationToken));
        var location = ApiRoutes.Link($"connections/{start.ConnectionId}/files/uploads/{upload.Id}");
        return TypedResults.Created(location, upload.ToInfo());
    }

    private static async Task<IResult> List(
        int id,
        [AsParameters] ConnectionFileServices files,
        [AsParameters] ConnectionUploadServices uploads,
        CancellationToken cancellationToken
    )
    {
        var (owner, answer) = await OwnerAsync(files, id, cancellationToken);
        if (owner is null)
            return answer!;
        return TypedResults.Ok(uploads.Uploads.List(owner).Select(u => u.ToInfo()).ToList());
    }

    private static async Task<IResult> Get(
        int id,
        string uploadId,
        [AsParameters] ConnectionFileServices files,
        [AsParameters] ConnectionUploadServices uploads,
        CancellationToken cancellationToken
    )
    {
        var (owner, answer) = await OwnerAsync(files, id, cancellationToken);
        if (owner is null)
            return answer!;
        return uploads.Uploads.Find(uploadId, owner) is { } upload
            ? TypedResults.Ok(upload.ToInfo())
            : TypedResults.NotFound();
    }

    private static async Task<IResult> Append(
        int id,
        string uploadId,
        long offset,
        [AsParameters] ConnectionFileServices files,
        [AsParameters] ConnectionUploadServices uploads
    )
    {
        var cancellationToken = files.Context.RequestAborted;
        var (open, answer) = await files.OpenAsync(id, Rights.UploadFiles, write: true, cancellationToken);
        if (open is null)
            return answer!;
        var owner = OwnerOf(id, await files.Current.GetAsync());
        if (uploads.Uploads.Find(uploadId, owner) is not { } upload)
            return TypedResults.NotFound();

        await upload.Gate.WaitAsync(cancellationToken);
        try
        {
            return await ConnectionUploadSteps.AppendAsync(new(files, open, uploads, upload), offset);
        }
        finally
        {
            upload.Gate.Release();
        }
    }

    private static async Task<IResult> Finish(
        int id,
        string uploadId,
        UploadFinish? request,
        [AsParameters] ConnectionFileServices files,
        [AsParameters] ConnectionUploadServices uploads
    )
    {
        var cancellationToken = files.Context.RequestAborted;
        var (open, answer) = await files.OpenAsync(id, Rights.UploadFiles, write: true, cancellationToken);
        if (open is null)
            return answer!;
        var state = await files.Current.GetAsync();
        if (uploads.Uploads.Find(uploadId, OwnerOf(id, state)) is not { } upload)
            return TypedResults.NotFound();
        var choice = request?.Conflicts ?? ConflictChoice.Ask;
        if (choice == ConflictChoice.Replace && !state.Has(Rights.DeleteFiles))
            return TypedResults.Problem(
                "Replacing needs the right to delete.",
                statusCode: StatusCodes.Status403Forbidden
            );

        await upload.Gate.WaitAsync(cancellationToken);
        try
        {
            return await ConnectionUploadSteps.FinishAsync(new(files, open, uploads, upload), choice);
        }
        finally
        {
            upload.Gate.Release();
        }
    }

    private static async Task<IResult> Cancel(
        int id,
        string uploadId,
        [AsParameters] ConnectionFileServices files,
        [AsParameters] ConnectionUploadServices uploads,
        CancellationToken cancellationToken
    )
    {
        var (owner, answer) = await OwnerAsync(files, id, cancellationToken);
        if (owner is null)
            return answer!;
        if (uploads.Uploads.Find(uploadId, owner) is not { } upload)
            return TypedResults.NotFound();
        // The provider ends the session itself (Dropbox after a week): nothing of it is a file yet.
        uploads.Uploads.Remove(upload);
        return TypedResults.NoContent();
    }

    /// <summary>
    /// Whose uploads the caller asks for, without opening the connection: one it may use (else 404), with
    /// <c>files.upload</c> (else 403).
    /// </summary>
    private static async Task<(RemoteUploadOwner? Owner, IResult? Answer)> OwnerAsync(
        ConnectionFileServices files,
        int id,
        CancellationToken cancellationToken
    )
    {
        var state = await files.Current.GetAsync();
        var usable = await files.Access.FindAsync(ConnectionEndpoints.SubjectOf(state), id, cancellationToken);
        if (usable is null)
            return (null, TypedResults.NotFound());
        if (!state.Has(Rights.UploadFiles))
            return (null, TypedResults.Forbid());
        return (OwnerOf(id, state), null);
    }

    /// <summary>The caller as an owner of uploads; visitors without an account have none (an id no one has).</summary>
    private static RemoteUploadOwner OwnerOf(int id, AccessState state) =>
        new(id, state.TenantId, state.User?.Id ?? -1);

    private static async Task<bool> IsFolderAsync(IRemoteStorage storage, string path, CancellationToken token) =>
        path.Length == 0 || await storage.GetAsync(path, token) is { IsFolder: true };

    private static IResult Invalid(string field, string message) =>
        TypedResults.ValidationProblem(new Dictionary<string, string[]> { [field] = [message] });

    private static IResult NotSupported() =>
        TypedResults.Problem(
            "The provider cannot take a file in parts.",
            statusCode: StatusCodes.Status409Conflict,
            type: ConnectionProblems.NotSupportedType
        );

    // Too many unfinished uploads: not a 409, which the client reads as a state (as the library's uploads).
    private static IResult TooMany() =>
        TypedResults.Problem(
            $"You have {RemoteUploads.MaxPerOwner} unfinished uploads: finish or cancel some first.",
            statusCode: StatusCodes.Status429TooManyRequests
        );
}
