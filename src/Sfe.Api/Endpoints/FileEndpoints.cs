using Microsoft.AspNetCore.Http.HttpResults;
using Sfe.Api.Security;
using Sfe.Core.Access;
using Sfe.Core.Files;
using Sfe.Core.Library;
using Microsoft.Extensions.Options;
using Sfe.Core.Logs;
using Sfe.Core.Options;
using Sfe.Core.Paths;
using Sfe.Core.Tagging;

namespace Sfe.Api.Endpoints;

/// <param name="Path">The folder to create the new one in (root-relative; empty for the root).</param>
/// <param name="Name">The new folder's name.</param>
public sealed record CreateFolderRequest(string? Path, string? Name);

/// <param name="Path">The file or folder to rename (root-relative).</param>
/// <param name="Name">Its new name, in the same folder.</param>
public sealed record RenameRequest(string? Path, string? Name);

/// <param name="Paths">The files and folders to delete (root-relative).</param>
/// <param name="Permanent">Skip the trash (always, when <c>Files:DeletePermanently</c> is on).</param>
public sealed record DeleteRequest(IReadOnlyList<string>? Paths, bool Permanent = false);

/// <param name="TrashId">Its entry in the trash, for an Undo; null when deleted for good or not deleted.</param>
public sealed record DeletedItem(string Path, FileOperationStatus Status, string? Message = null, int? TrashId = null);

public sealed record DeleteResults(IReadOnlyList<DeletedItem> Items, bool Permanent);

public sealed record EmptiedTrash(int Purged);

/// <summary>File management (agents/docs/development/file-management-plan.md): changes to the library's files.</summary>
public static class FileEndpoints
{
    /// <summary>The 409 problem types, which the client tells apart.</summary>
    public const string ExistsType = "file-exists";
    public const string ScanRunningType = "scan-running";
    public const string FolderRulesType = "folder-rules";

    /// <summary>Paths one delete request may name.</summary>
    public const int MaxDeletePaths = 1000;

    public static RouteGroupBuilder MapFileEndpoints(this RouteGroupBuilder api)
    {
        var files = api.MapGroup("/files").WithTags("Files");

        files.MapPost("/folders", CreateFolder)
            .RequireRight(Rights.CreateFolders)
            .FolderScoped("files.createFolders at the folder the new one goes in")
            .WithName("CreateFolder")
            .WithSummary("Creates a folder.")
            .WithDescription($"""
                Creates the folder `name` in the folder `path`, and indexes it at once when its parent is indexed.
                Answers the new folder as listings show it; 400 for a name that can't be used (with the reason), 404
                when the folder `path` is not there, 409 `{ExistsType}` when something of that name is (even one the
                caller doesn't see), 409 `{ScanRunningType}` while a scan runs over the folder.
                """);

        files.MapPost("/rename", Rename)
            .RequireRight(Rights.RenameFiles)
            .FolderScoped("files.rename at the file or folder, at its parent, and everywhere below a folder")
            .WithName("RenameItem")
            .WithSummary("Renames a file or folder, in its folder.")
            .WithDescription($"""
                Renames `path` to `name` in the same folder. A case-only change (`photo.jpg` → `Photo.jpg`) is
                allowed on Windows. What SFE keeps by path follows: the index (tags), folder access rules, quick links,
                settings naming folders, schedules, thumbnails and trickplay. A folder can be renamed only by someone
                who may rename everywhere below it. Answers the renamed item; 400 for a name that can't be used, 404,
                403, 409 `{ExistsType}`, `{ScanRunningType}` or `{FolderRulesType}` (rules are set where the folder would
                go), 500 when the disk refuses (a file in use, no write access).
                """);

        files.MapPost("/delete", Delete)
            .RequireRight(Rights.DeleteFiles, Rights.DeletePermanently)
            .FolderScoped("files.delete (files.deletePermanently to skip the trash) at each item, at its parent, and everywhere below a folder")
            .WithName("DeleteItems")
            .WithSummary("Deletes files and folders, to the trash or for good.")
            .WithDescription($"""
                Deletes each of `paths` (at most {MaxDeletePaths}): to the space's trash, or for good with `permanent`
                (always when `Files:DeletePermanently` is on, which then needs `files.deletePermanently`). A link is
                deleted as a link. Answers 200 with a result per path: `done` (with `trashId` when it went to the trash),
                `notFound` (also what the caller doesn't see), `forbidden`, `scanRunning`, `trashUnavailable` (a folder
                with links on another drive than the trash: delete it permanently instead), `failed` (in use, no write
                access), with a message.
                """);

        var trash = files.MapGroup("/trash");
        trash.MapGet("", ListTrash)
            .RequireRight(Rights.DeleteFiles, Rights.DeletePermanently)
            .FolderScoped("lists what the caller deleted, and what was deleted where the caller may delete")
            .WithName("ListTrash")
            .WithSummary("The space's trash, newest first.")
            .WithDescription("Pages of at most 200; `before` is the `next` of the previous page.");
        trash.MapPost("/{id:int}/restore", Restore)
            .RequireRight(Rights.DeleteFiles)
            .FolderScoped("files.delete where it was")
            .WithName("RestoreFromTrash")
            .WithSummary("Puts a file or folder of the trash back where it was.")
            .WithDescription($"""
                Back with the tags of its files and a folder's access rules; its folder is made again if it is gone, and
                ` (2)` is added to the name when it is taken there. Answers the restored item; 404, 409 `{ScanRunningType}`,
                500 when the disk refuses.
                """);
        trash.MapDelete("/{id:int}", Purge)
            .RequireRight(Rights.DeletePermanently)
            .FolderScoped("files.deletePermanently where it was")
            .WithName("DeleteFromTrash")
            .WithSummary("Deletes a file or folder of the trash for good.");
        trash.MapDelete("", Empty)
            .RequireRight(Rights.DeletePermanently)
            .FolderScoped("what the caller sees in the trash and may delete permanently where it was")
            .WithName("EmptyTrash")
            .WithSummary("Deletes for good everything of the trash the caller may.");

        return api;
    }

    private static async Task<Ok<DeleteResults>> Delete(
        FileOperations operations, LibraryPaths paths, ServerFiles serverFiles, WorkFolder workFolder, CurrentAccess current,
        IOptionsMonitor<SfeOptions> options, ILoggerFactory loggers, DeleteRequest request, CancellationToken cancellationToken)
    {
        var state = await current.GetAsync();
        var permanent = request.Permanent || options.CurrentValue.Files.DeletePermanently;
        var right = permanent ? Rights.DeletePermanently : Rights.DeleteFiles;
        var results = new List<DeletedItem>();
        foreach (var path in (request.Paths ?? []).Distinct(StringComparer.Ordinal).Take(MaxDeletePaths))
        {
            if (DeleteVerdict(state, paths, serverFiles, workFolder, path, right, loggers) is { } refused)
            {
                results.Add(new DeletedItem(path, refused));
                continue;
            }
            var result = await operations.DeleteAsync(path, permanent, Actor(state), cancellationToken);
            results.Add(new DeletedItem(path, result.Status, result.Message, result.TrashId));
        }
        return TypedResults.Ok(new DeleteResults(results, permanent));
    }

    /// <summary>Why the caller may not delete <paramref name="path"/> (with the right named); null when they may.</summary>
    private static FileOperationStatus? DeleteVerdict(
        AccessState state, LibraryPaths paths, ServerFiles serverFiles, WorkFolder workFolder, string? path, string right, ILoggerFactory loggers)
    {
        if (state.Check(paths, path, right) is var verdict and not PathVerdict.Allowed)
        {
            Refused(verdict, state, paths, path, "delete", loggers);
            return verdict == PathVerdict.NotFound ? FileOperationStatus.NotFound : FileOperationStatus.Forbidden;
        }
        if (!paths.TryResolve(path, out var full) || paths.ToRelative(full) is not { Length: > 0 } canonical)
            return FileOperationStatus.NotFound;
        if (Protected(full, serverFiles, workFolder))
        {
            Refused(PathVerdict.NotFound, state, paths, path, "delete", loggers, protectedPlace: true);
            return FileOperationStatus.NotFound;
        }
        if (state.Check(paths, ParentOf(canonical), right) != PathVerdict.Allowed)
            return FileOperationStatus.Forbidden;
        return Directory.Exists(full) && !state.Access.FolderPolicy.AllowsThroughout(canonical, right, state.Access.Rights)
            ? FileOperationStatus.Forbidden
            : null;
    }

    private static async Task<Ok<TrashPage>> ListTrash(
        Trash trash, LibraryPaths paths, CurrentAccess current, CancellationToken cancellationToken, int? before = null, int? limit = null)
    {
        var state = await current.GetAsync();
        return TypedResults.Ok(await trash.ListAsync(before, limit, item => SeesInTrash(state, paths, item), cancellationToken));
    }

    private static async Task<Results<Ok<LibraryItem>, NotFound, ForbidHttpResult, ProblemHttpResult>> Restore(
        Trash trash, TagService tags, LibraryPaths paths, CurrentAccess current, int id, CancellationToken cancellationToken)
    {
        var state = await current.GetAsync();
        if (await trash.FindAsync(id, cancellationToken) is not { } item || !SeesInTrash(state, paths, item))
            return TypedResults.NotFound();
        if (state.Check(paths, ParentOf(item.Path), Rights.DeleteFiles) != PathVerdict.Allowed)
            return TypedResults.Forbid();
        var result = await trash.RestoreAsync(id, Actor(state), cancellationToken);
        return result.Status switch
        {
            FileOperationStatus.Done => TypedResults.Ok((await tags.AttachIndexDataAsync([result.Item!], cancellationToken))[0]),
            FileOperationStatus.NotFound => TypedResults.Problem(result.Message, statusCode: StatusCodes.Status404NotFound),
            FileOperationStatus.ScanRunning => TypedResults.Problem(result.Message, statusCode: StatusCodes.Status409Conflict, type: ScanRunningType),
            FileOperationStatus.Exists => TypedResults.Problem(result.Message, statusCode: StatusCodes.Status409Conflict, type: ExistsType),
            _ => TypedResults.Problem(result.Message, statusCode: StatusCodes.Status500InternalServerError),
        };
    }

    private static async Task<Results<NoContent, NotFound, ForbidHttpResult>> Purge(
        Trash trash, LibraryPaths paths, CurrentAccess current, int id, CancellationToken cancellationToken)
    {
        var state = await current.GetAsync();
        if (await trash.FindAsync(id, cancellationToken) is not { } item || !SeesInTrash(state, paths, item))
            return TypedResults.NotFound();
        if (state.Check(paths, ParentOf(item.Path), Rights.DeletePermanently) != PathVerdict.Allowed)
            return TypedResults.Forbid();
        return await trash.PurgeAsync(id, Actor(state), cancellationToken) ? TypedResults.NoContent() : TypedResults.NotFound();
    }

    private static async Task<Ok<EmptiedTrash>> Empty(Trash trash, LibraryPaths paths, CurrentAccess current, CancellationToken cancellationToken)
    {
        var state = await current.GetAsync();
        var purged = 0;
        // What the caller sees and may delete for good where it was, page by page (each page's rows go as it is purged).
        while (true)
        {
            var page = await trash.ListAsync(null, Trash.MaxPageSize,
                item => SeesInTrash(state, paths, item) && state.Check(paths, ParentOf(item.Path), Rights.DeletePermanently) == PathVerdict.Allowed,
                cancellationToken);
            var before = purged;
            foreach (var item in page.Items)
            {
                if (await trash.PurgeAsync(item.Id, Actor(state), cancellationToken))
                    purged++;
            }
            if (!page.HasMore || purged == before)
                break;
        }
        return TypedResults.Ok(new EmptiedTrash(purged));
    }

    /// <summary>
    /// The caller sees a trash entry: one they deleted, or one deleted where they may delete (to the trash or for good);
    /// server administrators see them all. A folder that had access rules of its own (kept in the entry until a restore)
    /// may have been hidden from people who may delete in its parent: only those who manage the space's access see it.
    /// </summary>
    private static bool SeesInTrash(AccessState state, LibraryPaths paths, TrashItem item) =>
        state.Access.IsServerAdmin
        || (state.User is { } user && item.DeletedById == user.Id)
        || (item.HasRules
            ? state.Has(Rights.ManageMembers)
            : state.Check(paths, ParentOf(item.Path), Rights.DeleteFiles) == PathVerdict.Allowed
                || state.Check(paths, ParentOf(item.Path), Rights.DeletePermanently) == PathVerdict.Allowed);

    private static async Task<Results<Ok<LibraryItem>, ValidationProblem, NotFound, ForbidHttpResult, ProblemHttpResult>> CreateFolder(
        FileOperations operations, TagService tags, LibraryPaths paths, ServerFiles serverFiles, WorkFolder workFolder, CurrentAccess current,
        ILoggerFactory loggers, CreateFolderRequest request, CancellationToken cancellationToken)
    {
        var state = await current.GetAsync();
        if (state.Check(paths, request.Path, Rights.CreateFolders) is var verdict and not PathVerdict.Allowed)
            return Refused(verdict, state, paths, request.Path, "create a folder in", loggers);
        if (paths.TryResolve(request.Path, out var folder) && Protected(folder, serverFiles, workFolder))
            return Refused(PathVerdict.NotFound, state, paths, request.Path, "create a folder in", loggers, protectedPlace: true);

        return await ResultAsync(await operations.CreateFolderAsync(request.Path, request.Name, Actor(state), cancellationToken), tags, cancellationToken);
    }

    private static async Task<Results<Ok<LibraryItem>, ValidationProblem, NotFound, ForbidHttpResult, ProblemHttpResult>> Rename(
        FileOperations operations, TagService tags, LibraryPaths paths, ServerFiles serverFiles, WorkFolder workFolder, CurrentAccess current,
        ILoggerFactory loggers, RenameRequest request, CancellationToken cancellationToken)
    {
        var state = await current.GetAsync();
        if (state.Check(paths, request.Path, Rights.RenameFiles) is var verdict and not PathVerdict.Allowed)
            return Refused(verdict, state, paths, request.Path, "rename", loggers);
        // Renaming changes the folder's list of names too; a folder carries everything below it, which must all be the
        // caller's to change (nobody renames away what they can't see or change).
        if (paths.TryResolve(request.Path, out var full) && paths.ToRelative(full) is { Length: > 0 } canonical)
        {
            if (Protected(full, serverFiles, workFolder) || Protected(Path.GetDirectoryName(full)!, serverFiles, workFolder))
                return Refused(PathVerdict.NotFound, state, paths, request.Path, "rename", loggers, protectedPlace: true);
            if (state.Check(paths, ParentOf(canonical), Rights.RenameFiles) != PathVerdict.Allowed)
                return TypedResults.Forbid();
            if (Directory.Exists(full) && !state.Access.FolderPolicy.AllowsThroughout(canonical, Rights.RenameFiles, state.Access.Rights))
                return TypedResults.Problem("Some folders inside it are not yours to change: it can't be renamed.", statusCode: StatusCodes.Status403Forbidden);
        }

        return await ResultAsync(await operations.RenameAsync(request.Path, request.Name, Actor(state), cancellationToken), tags, cancellationToken);
    }

    private static async Task<Results<Ok<LibraryItem>, ValidationProblem, NotFound, ForbidHttpResult, ProblemHttpResult>> ResultAsync(
        FileOperationResult result, TagService tags, CancellationToken cancellationToken) => result.Status switch
        {
            // With its tags and whether the index has it, as listings show it.
            FileOperationStatus.Done => TypedResults.Ok((await tags.AttachIndexDataAsync([result.Item!], cancellationToken))[0]),
            FileOperationStatus.NotFound => TypedResults.NotFound(),
            FileOperationStatus.Invalid => TypedResults.ValidationProblem(new Dictionary<string, string[]> { ["name"] = [result.Message!] }),
            FileOperationStatus.Exists => TypedResults.Problem(result.Message, statusCode: StatusCodes.Status409Conflict, type: ExistsType),
            FileOperationStatus.ScanRunning => TypedResults.Problem(result.Message, statusCode: StatusCodes.Status409Conflict, type: ScanRunningType),
            FileOperationStatus.Conflict => TypedResults.Problem(result.Message, statusCode: StatusCodes.Status409Conflict, type: FolderRulesType),
            // Not a 409: the client takes those as answers carrying the current state.
            _ => TypedResults.Problem(result.Message, statusCode: StatusCodes.Status500InternalServerError),
        };

    /// <summary>
    /// The answer to a request about a place the caller may not change (403) or doesn't see (404). An attempt on a place
    /// that is there but hidden from the caller (the page never offers it), or on the server's own files through a
    /// link, is logged as a security warning.
    /// </summary>
    private static Results<Ok<LibraryItem>, ValidationProblem, NotFound, ForbidHttpResult, ProblemHttpResult> Refused(
        PathVerdict verdict, AccessState state, LibraryPaths paths, string? path, string action, ILoggerFactory loggers, bool protectedPlace = false)
    {
        if (verdict == PathVerdict.Forbidden)
            return TypedResults.Forbid();
        if (protectedPlace || (paths.TryResolve(path, out var full) && (Directory.Exists(full) || File.Exists(full))))
            loggers.CreateLogger(typeof(FileEndpoints)).LogSecurityWarning("{User} was refused to {Action} “{Path}”: {Why}",
                state.User?.UserName ?? "A visitor", action, path, protectedPlace ? "it leads to the server's own files" : "a folder they may not see");
        return TypedResults.NotFound();
    }

    /// <summary>
    /// <paramref name="full"/> really is (through a folder link) among the server's own files or SFE's work folder:
    /// a link made in the library must never become a way to write keys, settings or the trash.
    /// </summary>
    private static bool Protected(string full, ServerFiles serverFiles, WorkFolder workFolder)
    {
        try
        {
            return serverFiles.Contains(full) || workFolder.Contains(LibraryPaths.RealPath(full));
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return true;
        }
    }

    private static FileActor Actor(AccessState state) =>
        new(state.User?.Id, state.User is { } user ? user.DisplayName ?? user.UserName : "A visitor");

    private static string ParentOf(string path) => path.LastIndexOf('/') is var slash and >= 0 ? path[..slash] : string.Empty;
}
