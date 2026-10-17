using Microsoft.AspNetCore.Http.HttpResults;
using Sfe.Api.Security;
using Sfe.Core.Access;
using Sfe.Core.Files;
using Sfe.Core.Library;
using Sfe.Core.Logs;
using Sfe.Core.Paths;
using Sfe.Core.Tagging;

namespace Sfe.Api.Endpoints;

/// <param name="Path">The folder to create the new one in (root-relative; empty for the root).</param>
/// <param name="Name">The new folder's name.</param>
public sealed record CreateFolderRequest(string? Path, string? Name);

/// <param name="Path">The file or folder to rename (root-relative).</param>
/// <param name="Name">Its new name, in the same folder.</param>
public sealed record RenameRequest(string? Path, string? Name);

/// <summary>File management (agents/docs/development/file-management-plan.md): changes to the library's files.</summary>
public static class FileEndpoints
{
    /// <summary>The 409 problem types, which the client tells apart.</summary>
    public const string ExistsType = "file-exists";
    public const string ScanRunningType = "scan-running";
    public const string FolderRulesType = "folder-rules";

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

        return api;
    }

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
