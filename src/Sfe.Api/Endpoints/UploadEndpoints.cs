using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.AspNetCore.Mvc;
using Sfe.Api.Security;
using Sfe.Core.Access;
using Sfe.Core.Files;
using Sfe.Core.Library;
using Sfe.Core.Paths;
using Sfe.Core.Tagging;

namespace Sfe.Api.Endpoints;

/// <param name="Target">The folder the files would go into.</param>
/// <param name="Names">Their names, or paths below it for a folder upload.</param>
public sealed record UploadCheckRequest(string? Target, IReadOnlyList<string>? Names);

/// <param name="Existing">The names (of <see cref="UploadCheckRequest.Names"/>) already taken there, seen by the caller or not.</param>
/// <param name="CanReplace">The caller may replace what is there (it would go to the trash).</param>
public sealed record UploadCheck(IReadOnlyList<string> Existing, bool CanReplace);

public sealed record UploadFinish(ConflictChoice Conflicts = ConflictChoice.Ask);

/// <summary>
/// Resumable uploads (agents/docs/features/uploads.md): check the names, start, send chunks at the offset the server
/// has, finish. Unfinished uploads are listed for their owner (to go on after a reload) and, with `settings.files`,
/// for the space's administrators.
/// </summary>
public static class UploadEndpoints
{
    /// <summary>Names one check may carry (a folder upload sends its files in several).</summary>
    public const int MaxCheckNames = 1000;

    public static RouteGroupBuilder MapUploadEndpoints(this RouteGroupBuilder api)
    {
        var uploads = api.MapGroup("/files/uploads").WithTags("Files");

        uploads.MapPost("/check", Check)
            .RequireRight(Rights.UploadFiles)
            .FolderScoped("files.upload at the folder; names taken are told whether the caller sees them or not")
            .WithName("CheckUploads")
            .WithSummary("Which of these names are already taken in the folder.")
            .WithDescription($"Asked before uploading, so that the person decides about names taken (Replace, Skip, Keep both) before sending anything. At most {MaxCheckNames} names.");
        uploads.MapPost("", Start)
            .RequireRight(Rights.UploadFiles)
            .FolderScoped("files.upload at the folder and at each folder a folder upload makes")
            .WithName("StartUpload")
            .WithSummary("Starts an upload.")
            .WithDescription($"""
                Checks the folder, the name (a path below the folder for a folder upload: its folders are made at the
                finish), the size (`Files:Uploads:MaxFileSizeMegabytes`, the free space kept) and answers the upload
                (201) with its `id`. Then `PUT /<id>?offset=` sends the bytes in chunks of at most
                {Uploads.MaxChunkBytes / 1024 / 1024} MB, and `POST /<id>/finish` puts the file in the folder.
                """);
        uploads.MapGet("", List)
            .RequireRight(Rights.UploadFiles, Rights.FileSettings)
            .NotLibraryPaths()
            .WithName("ListUploads")
            .WithSummary("The caller's unfinished uploads; with `all` (right `settings.files`), everyone's.");
        uploads.MapGet("/{id:regex(^[0-9a-f]{{32}}$)}", Get)
            .RequireRight(Rights.UploadFiles)
            .NotLibraryPaths()
            .WithName("GetUpload")
            .WithSummary("One of the caller's unfinished uploads: how much the server has.");
        uploads.MapPut("/{id:regex(^[0-9a-f]{{32}}$)}", Append)
            .RequireRight(Rights.UploadFiles)
            .NotLibraryPaths()
            .RequireRateLimiting(RequestLimits.Chunks)
            .WithMetadata(new RequestSizeLimitAttribute(Uploads.MaxChunkBytes + 1))
            .WithName("SendUploadChunk")
            .WithSummary("Appends a chunk (the body) at `offset`.")
            .WithDescription($"""
                `offset` must be the bytes the server has: another one answers 409 with the upload, to resend from
                there (a reply lost on the way). A chunk passing the size, or {Uploads.MaxChunkBytes / 1024 / 1024} MB,
                is refused (400). Answers the upload.
                """);
        uploads.MapPost("/{id:regex(^[0-9a-f]{{32}}$)}/finish", Finish)
            .RequireRight(Rights.UploadFiles)
            .FolderScoped("files.upload again (it may have changed), files.delete at a file replaced")
            .WithName("FinishUpload")
            .WithSummary("Puts a fully sent upload into its folder.")
            .WithDescription($"""
                `conflicts` says what to do when the name is taken: `ask` (409 `{FileEndpoints.ExistsType}`, to ask and
                finish again), `replace` (the file there goes to the trash; needs the right to delete there), `skip`
                (the upload is removed, 204), `keepBoth` (` (2)` added). Answers the new file as listings show it.
                """);
        uploads.MapDelete("/{id:regex(^[0-9a-f]{{32}}$)}", Cancel)
            .RequireRight(Rights.UploadFiles, Rights.FileSettings)
            .NotLibraryPaths()
            .WithName("CancelUpload")
            .WithSummary("Cancels an unfinished upload (the caller's, or anyone's with `settings.files`): what it received is removed.");

        return api;
    }

    private static async Task<Results<Ok<UploadCheck>, NotFound, ForbidHttpResult, ValidationProblem>> Check(
        LibraryPaths paths, ServerFiles serverFiles, WorkFolder work, Trash trash, CurrentAccess current, UploadCheckRequest request)
    {
        var state = await current.GetAsync();
        if (request.Names is not { Count: > 0 and <= MaxCheckNames } names)
            return TypedResults.ValidationProblem(new Dictionary<string, string[]> { ["names"] = [$"Name 1 to {MaxCheckNames} files."] });
        if (state.Check(paths, request.Target, Rights.UploadFiles) is var verdict and not PathVerdict.Allowed)
            return verdict == PathVerdict.Forbidden ? TypedResults.Forbid() : TypedResults.NotFound();
        if (!paths.TryResolve(request.Target, out var folder) || !Directory.Exists(folder) || FileEndpoints.Protected(folder, serverFiles, work))
            return TypedResults.NotFound();
        var existing = names.Where(name => !string.IsNullOrEmpty(name) && !name.Split('/', '\\').Any(s => s is "" or "." or "..")
                && Path.Join(folder, name.Replace('\\', '/')) is var full && (File.Exists(full) || Directory.Exists(full)))
            .Distinct(StringComparer.Ordinal).ToList();
        return TypedResults.Ok(new UploadCheck(existing, state.Check(paths, request.Target, trash.AlwaysPermanent ? Rights.DeletePermanently : Rights.DeleteFiles) == PathVerdict.Allowed));
    }

    private static async Task<Results<Created<UploadInfo>, ProblemHttpResult, ValidationProblem, NotFound, ForbidHttpResult>> Start(
        Uploads uploads, LibraryPaths paths, ServerFiles serverFiles, WorkFolder work, CurrentAccess current, UploadStart request, CancellationToken cancellationToken)
    {
        var state = await current.GetAsync();
        var result = await uploads.StartAsync(request, Actor(state), new UploadAccess(state, paths, serverFiles, work), cancellationToken);
        return result.Status switch
        {
            FileOperationStatus.Done => TypedResults.Created($"api/files/uploads/{result.Upload!.Id}", result.Upload),
            FileOperationStatus.NotFound => TypedResults.NotFound(),
            FileOperationStatus.Forbidden => TypedResults.Forbid(),
            FileOperationStatus.Invalid => TypedResults.ValidationProblem(new Dictionary<string, string[]> { ["name"] = [result.Message!] }),
            // Too many unfinished uploads: not a 409, which the client reads as a state.
            FileOperationStatus.Conflict => TypedResults.Problem(result.Message, statusCode: StatusCodes.Status429TooManyRequests),
            _ => TypedResults.Problem(result.Message, statusCode: StatusCodes.Status507InsufficientStorage),
        };
    }

    private static async Task<Results<Ok<IReadOnlyList<UploadInfo>>, ForbidHttpResult>> List(Uploads uploads, CurrentAccess current, bool all = false)
    {
        var state = await current.GetAsync();
        if (all ? !state.Has(Rights.FileSettings) : !state.Has(Rights.UploadFiles))
            return TypedResults.Forbid();
        return TypedResults.Ok(uploads.List(state.User?.Id, all));
    }

    private static async Task<Results<Ok<UploadInfo>, NotFound>> Get(Uploads uploads, CurrentAccess current, string id) =>
        Mine(uploads.Find(id), await current.GetAsync()) is { } upload ? TypedResults.Ok(upload) : TypedResults.NotFound();

    private static async Task<Results<Ok<UploadInfo>, NotFound, Conflict<UploadInfo>, ProblemHttpResult>> Append(
        Uploads uploads, CurrentAccess current, HttpRequest request, string id, long offset, CancellationToken cancellationToken)
    {
        if (Mine(uploads.Find(id), await current.GetAsync()) is null)
            return TypedResults.NotFound();
        var result = await uploads.AppendAsync(id, offset, request.Body, cancellationToken);
        return result.Status switch
        {
            FileOperationStatus.Done => TypedResults.Ok(result.Upload!),
            FileOperationStatus.Conflict => TypedResults.Conflict(result.Upload!),
            FileOperationStatus.NotFound => TypedResults.NotFound(),
            _ => TypedResults.Problem(result.Message, statusCode: StatusCodes.Status400BadRequest),
        };
    }

    private static async Task<Results<Ok<LibraryItem>, NoContent, NotFound, ProblemHttpResult, ValidationProblem>> Finish(
        Uploads uploads, TagService tags, LibraryPaths paths, ServerFiles serverFiles, WorkFolder work, CurrentAccess current, string id, UploadFinish? request,
        CancellationToken cancellationToken)
    {
        var state = await current.GetAsync();
        if (Mine(uploads.Find(id), state) is null)
            return TypedResults.NotFound();
        var result = await uploads.FinishAsync(id, request?.Conflicts ?? ConflictChoice.Ask, Actor(state), new UploadAccess(state, paths, serverFiles, work), cancellationToken);
        return result.Status switch
        {
            FileOperationStatus.Done => TypedResults.Ok((await tags.AttachIndexDataAsync([result.Item!], cancellationToken))[0]),
            FileOperationStatus.Skipped => TypedResults.NoContent(),
            FileOperationStatus.NotFound => TypedResults.NotFound(),
            FileOperationStatus.Invalid => TypedResults.ValidationProblem(new Dictionary<string, string[]> { ["name"] = [result.Message!] }),
            FileOperationStatus.Forbidden => TypedResults.Problem(result.Message, statusCode: StatusCodes.Status403Forbidden),
            FileOperationStatus.Exists => TypedResults.Problem(result.Message, statusCode: StatusCodes.Status409Conflict, type: FileEndpoints.ExistsType),
            FileOperationStatus.ScanRunning => TypedResults.Problem(result.Message, statusCode: StatusCodes.Status409Conflict, type: FileEndpoints.ScanRunningType),
            _ => TypedResults.Problem(result.Message, statusCode: StatusCodes.Status500InternalServerError),
        };
    }

    private static async Task<Results<NoContent, NotFound>> Cancel(Uploads uploads, CurrentAccess current, string id)
    {
        var state = await current.GetAsync();
        var upload = uploads.Find(id);
        if (upload is null || (Mine(upload, state) is null && !state.Has(Rights.FileSettings)))
            return TypedResults.NotFound();
        uploads.Cancel(id, Actor(state));
        return TypedResults.NoContent();
    }

    /// <summary>The upload when it is the caller's (visitors without an account have none: they can't have the right).</summary>
    private static UploadInfo? Mine(UploadInfo? upload, AccessState state) =>
        upload is not null && state.User is { } user && upload.OwnerId == user.Id && state.Has(Rights.UploadFiles) ? upload : null;

    private static FileActor Actor(AccessState state) =>
        new(state.User?.Id, state.User is { } user ? user.DisplayName ?? user.UserName : "A visitor");

    /// <summary>The caller's access in this request.</summary>
    private sealed class UploadAccess(AccessState state, LibraryPaths paths, ServerFiles serverFiles, WorkFolder work) : IUploadAccess
    {
        public Task<bool> MayAsync(string path, string right) => Task.FromResult(state.Check(paths, path, right) == PathVerdict.Allowed);

        public bool IsProtected(string fullPath) => FileEndpoints.Protected(fullPath, serverFiles, work);
    }
}
