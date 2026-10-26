using Microsoft.AspNetCore.Http.HttpResults;
using Sfe.Api.Security;
using Sfe.Core.Access;
using Sfe.Core.Data;
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

public sealed record CreateFileRequest(string? Path, string? Name);

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

/// <param name="Paths">What would move.</param>
/// <param name="Target">The folder it would go into.</param>
public sealed record MoveCheckRequest(IReadOnlyList<string>? Paths, string? Target);

/// <summary>Someone who would see or change more of a moved folder than before (its new parent gives more access).</summary>
/// <param name="Who">The group's or account's name ("Visitors" for the Guests group).</param>
public sealed record AccessGain(string Folder, string Who, bool IsGroup, FolderLevel Before, FolderLevel After);

public sealed record MoveCheck(IReadOnlyList<AccessGain> Gains);

public sealed record JobAnswer(ConflictChoice Choice, bool ForAll = false);

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

        files.MapPost("/new-file", CreateFile)
            .RequireRight(Rights.CreateFiles)
            .FolderScoped("files.createFiles at the folder the new file goes in")
            .WithName("CreateFile")
            .WithSummary("Creates an empty file.")
            .WithDescription($"""
                Creates the empty file `name` in the folder `path` (never over anything: 409 `{ExistsType}` when the name
                is taken, even by an item the caller doesn't see) and indexes it. Answers the new file as listings show it;
                400 for a name that can't be used, 404 when the folder is not there, 409 `{ScanRunningType}` during a scan.
                """);
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

        var jobs = files.MapGroup("/jobs");
        jobs.MapPost("/check", CheckMove)
            .RequireRight(Rights.MoveFiles)
            .FolderScoped("only the folders the caller may move, seen by their rules")
            .WithName("CheckMove")
            .WithSummary("Who would gain access to folders if they moved there.")
            .WithDescription("Each group, and each account with rules, that would see or change more of a moved folder than now: the client asks for a confirmation (and offers to keep the current access) before starting the move.");
        jobs.MapPost("", StartJob)
            .RequireRight(Rights.MoveFiles, Rights.CopyFiles)
            .FolderScoped("files.move at each item, its parent, below a folder and at the target; files.copy at the target, files.view at what is copied (checked again at each item as the job runs)")
            .WithName("StartFileJob")
            .WithSummary("Moves or copies files and folders, in the background.")
            .WithDescription($"""
                Starts a job and answers its first state (202). `conflicts` says what to do when the target holds an item of
                the same name: `ask` (the job waits: `GET` shows the `conflict`, `POST /answer` replies), `replace` (needs
                the right to delete there: the replaced item goes to the trash), `skip`, `keepBoth` (` (2)` added). Moves
                keep tags, folder rules (`keepAccess` pins each group's current access), quick links and caches; copies
                never follow links, leave out folders the caller may not see, and get the tags of their originals with
                `keepTags`. Results per item in `results`.
                """);
        jobs.MapGet("", ListJobs)
            .RequireRight(Rights.MoveFiles, Rights.CopyFiles, Rights.ExtractFiles)
            .NotLibraryPaths()
            .WithName("ListFileJobs")
            .WithSummary("The caller's moves, copies and extractions, running and recent.");
        jobs.MapGet("/{id:int}", GetJob)
            .RequireRight(Rights.MoveFiles, Rights.CopyFiles, Rights.ExtractFiles)
            .NotLibraryPaths()
            .WithName("GetFileJob")
            .WithSummary("One of the caller's moves or copies.");
        jobs.MapPost("/{id:int}/answer", AnswerJob)
            .RequireRight(Rights.MoveFiles, Rights.CopyFiles, Rights.ExtractFiles)
            .NotLibraryPaths()
            .WithName("AnswerFileJob")
            .WithSummary("Answers a job's question about a name clash (`forAll`: the same for the next ones).");
        jobs.MapPost("/{id:int}/cancel", CancelJob)
            .RequireRight(Rights.MoveFiles, Rights.CopyFiles, Rights.ExtractFiles)
            .NotLibraryPaths()
            .WithName("CancelFileJob")
            .WithSummary("Stops a move or copy: what is done stays, nothing half copied does.");

        var trash = files.MapGroup("/trash");
        trash.MapGet("", ListTrash)
            .RequireRight(Rights.DeleteFiles, Rights.DeletePermanently)
            .FolderScoped("lists what the caller deleted, and what was deleted where the caller may delete")
            .WithName("ListTrash")
            .WithSummary("The space's trash, newest first.")
            .WithDescription("Pages of at most 200; `before` is the `next` of the previous page.");
        trash.MapGet("/count", CountTrash)
            .RequireRight(Rights.DeleteFiles, Rights.DeletePermanently)
            .FolderScoped("counts what the caller deleted, and what was deleted where the caller may delete")
            .WithName("CountTrash")
            .WithSummary("How many files and folders of the trash the caller sees, for the sidebar's Trash link.")
            .WithDescription($"Counted up to {Trash.CountCap}; `more` when there are more.");
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

    private static async Task<Ok<TrashCount>> CountTrash(Trash trash, LibraryPaths paths, CurrentAccess current, CancellationToken cancellationToken)
    {
        var state = await current.GetAsync();
        return TypedResults.Ok(await trash.CountAsync(item => SeesInTrash(state, paths, item), cancellationToken));
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

    private static async Task<Results<Ok<MoveCheck>, ValidationProblem>> CheckMove(
        FolderRuleService rules, AccessService accounts, LibraryPaths paths, CurrentAccess current, MoveCheckRequest request, CancellationToken cancellationToken)
    {
        var state = await current.GetAsync();
        if (state.Tenant is not { } tenant || !paths.TryResolve(request.Target, out var targetFull) || paths.ToRelative(targetFull) is not { } target)
            return TypedResults.ValidationProblem(new Dictionary<string, string[]> { ["target"] = ["Not a folder of the library."] });
        var groups = (await accounts.GetGroupsAsync(tenant.Id, cancellationToken)).ToDictionary(g => g.Id);
        var gains = new List<AccessGain>();
        foreach (var path in request.Paths ?? [])
        {
            if (!paths.TryResolve(path, out var full) || !Directory.Exists(full) || paths.ToRelative(full) is not { Length: > 0 } folder
                || state.Check(paths, folder, Rights.MoveFiles) != PathVerdict.Allowed)
                continue;
            var name = folder.Split('/')[^1];
            var compared = await rules.CompareMoveAsync(tenant.Id, folder, target.Length == 0 ? name : $"{target}/{name}", cancellationToken);
            foreach (var change in compared.Value?.Where(c => c.Gains) ?? [])
            {
                var who = change.Principal.GroupId is { } groupId
                    ? groups.TryGetValue(groupId, out var group) ? group.BuiltIn == BuiltInGroup.Guests ? "Visitors" : group.Name : "A group"
                    : (await accounts.GetUserAsync(change.Principal.UserId!.Value, cancellationToken)) is { } user ? user.DisplayName ?? user.UserName : "An account";
                gains.Add(new AccessGain(folder, who, change.Principal.GroupId is not null, change.Before ?? FolderLevel.Hidden, change.After ?? FolderLevel.Hidden));
            }
        }
        return TypedResults.Ok(new MoveCheck(gains));
    }

    private static async Task<Results<Accepted<FileJobView>, ForbidHttpResult, ValidationProblem>> StartJob(
        FileJobs jobs, AccessService accounts, RightsEvaluator rights, LibraryPaths paths, ServerFiles serverFiles, WorkFolder work, CurrentAccess current,
        FileJobRequest request)
    {
        var state = await current.GetAsync();
        if (state.Tenant is not { } tenant)
            return TypedResults.Forbid();
        if (request.Kind == FileJobKind.Extract)
            return TypedResults.ValidationProblem(new Dictionary<string, string[]> { ["kind"] = ["Archives are extracted through api/files/jobs/extract."] });
        if (!state.Has(request.Kind == FileJobKind.Move ? Rights.MoveFiles : Rights.CopyFiles))
            return TypedResults.Forbid();
        if (request.Paths is not { Count: > 0 } || request.Paths.Count > MaxDeletePaths)
            return TypedResults.ValidationProblem(new Dictionary<string, string[]> { ["paths"] = [$"Name 1 to {MaxDeletePaths} files or folders."] });
        // Every item is checked as the job reaches it, with the person's access then.
        var access = new FileJobAccess(accounts, rights, paths, serverFiles, work, tenant, state.User?.Id);
        var view = jobs.Start(request, Actor(state), access);
        return TypedResults.Accepted($"api/files/jobs/{view.Id}", view);
    }

    private static async Task<Ok<IReadOnlyList<FileJobView>>> ListJobs(FileJobs jobs, CurrentAccess current) =>
        TypedResults.Ok(jobs.List((await current.GetAsync()).User?.Id ?? -1));

    private static async Task<Results<Ok<FileJobView>, NotFound>> GetJob(FileJobs jobs, CurrentAccess current, int id) =>
        jobs.Find(id) is { } job && job.OwnerId == (await current.GetAsync()).User?.Id ? TypedResults.Ok(job) : TypedResults.NotFound();

    private static async Task<Results<NoContent, NotFound, Conflict>> AnswerJob(FileJobs jobs, CurrentAccess current, int id, JobAnswer answer)
    {
        if (jobs.Find(id) is not { } job || job.OwnerId != (await current.GetAsync()).User?.Id)
            return TypedResults.NotFound();
        return jobs.Answer(id, answer.Choice, answer.ForAll) ? TypedResults.NoContent() : TypedResults.Conflict();
    }

    private static async Task<Results<NoContent, NotFound>> CancelJob(FileJobs jobs, CurrentAccess current, int id, CancellationToken cancellationToken)
    {
        if (jobs.Find(id) is not { } job || job.OwnerId != (await current.GetAsync()).User?.Id)
            return TypedResults.NotFound();
        jobs.Cancel(id);
        return TypedResults.NoContent();
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

    private static async Task<Results<Ok<LibraryItem>, ValidationProblem, NotFound, ForbidHttpResult, ProblemHttpResult>> CreateFile(
        FileOperations operations, TagService tags, LibraryPaths paths, ServerFiles serverFiles, WorkFolder workFolder, CurrentAccess current,
        ILoggerFactory loggers, CreateFileRequest request, CancellationToken cancellationToken)
    {
        var state = await current.GetAsync();
        if (state.Check(paths, request.Path, Rights.CreateFiles) is var verdict and not PathVerdict.Allowed)
            return Refused(verdict, state, paths, request.Path, "create a file in", loggers);
        if (paths.TryResolve(request.Path, out var folder) && Protected(folder, serverFiles, workFolder))
            return Refused(PathVerdict.NotFound, state, paths, request.Path, "create a file in", loggers, protectedPlace: true);

        return await ResultAsync(await operations.CreateFileAsync(request.Path, request.Name, Actor(state), cancellationToken), tags, cancellationToken);
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

    internal static async Task<Results<Ok<LibraryItem>, ValidationProblem, NotFound, ForbidHttpResult, ProblemHttpResult>> ResultAsync(
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
    internal static Results<Ok<LibraryItem>, ValidationProblem, NotFound, ForbidHttpResult, ProblemHttpResult> Refused(
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
    internal static bool Protected(string full, ServerFiles serverFiles, WorkFolder workFolder)
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

    internal static FileActor Actor(AccessState state) =>
        new(state.User?.Id, state.User is { } user ? user.DisplayName ?? user.UserName : "A visitor");

    private static string ParentOf(string path) => path.LastIndexOf('/') is var slash and >= 0 ? path[..slash] : string.Empty;
}
