using Microsoft.AspNetCore.Http.HttpResults;
using Otools.Net.Sfe.Api.Security;
using Otools.Net.Sfe.Core.Access;
using Otools.Net.Sfe.Core.Files;
using Otools.Net.Sfe.Core.Library;
using Otools.Net.Sfe.Core.Tagging;

namespace Otools.Net.Sfe.Api.Endpoints;

/// <summary>
/// The previous versions of a library file (agents/docs/features/previous-versions.md): the copies the trash keeps
/// of its content before it was saved over, listed, opened, restored as a copy or over the file. A version is the
/// file's: who may see the file sees its versions, wherever the trash's own rules would show the entry.
/// </summary>
public static class VersionEndpoints
{
    private const string ListDescription = """
        The copies of the file's content kept in the trash before it was saved over (text editor, picture editor),
        newest first: `deletedAt` is when it was saved over, `deletedBy` who saved it. `restoreOver`,
        `restoreCopy` and `download` say what the caller may do with them. A version follows its file when it is
        renamed or moved in SFE; it is purged with the trash.
        """;

    private const string RestoreDescription = """
        Puts the version in place of the file, swapped in whole: the content the file has first becomes a version
        itself (`trashId`), the version restored leaves the list. The file keeps its tags. Needs the rights of
        saving over the file (files.delete at it and its folder, files.createFiles in its folder), in a space that
        keeps deleted files in the trash. 409 `scan-running` while a scan works there.
        """;

    public static RouteGroupBuilder MapVersionEndpoints(this RouteGroupBuilder api)
    {
        var versions = api.MapGroup("/files/versions").WithTags("Files");
        versions
            .MapGet("", List)
            .RequireRight(Rights.ViewFiles)
            .FolderScoped("files.view at the file")
            .WithName("ListFileVersions")
            .WithSummary("The previous versions of a file, kept in the trash when it was saved over.")
            .WithDescription(ListDescription);
        versions
            .MapGet("/{id:int}/content", Content)
            .RequireRight(Rights.ViewFiles)
            .NeverRunByPages()
            .FolderScoped("files.view (files.download to download) at the file")
            .WithName("GetFileVersion")
            .WithSummary("Streams a previous version of a file.")
            .WithDescription("`download=true` asks the browser to save it, which needs the right `files.download`.");
        versions
            .MapPost("/{id:int}/copy", RestoreCopy)
            .RequireRight(Rights.CreateFiles)
            .FolderScoped("files.view at the file, files.createFiles in its folder")
            .WithName("RestoreFileVersionAsCopy")
            .WithSummary("Copies a previous version next to its file (\" (2)\" added to the name).");
        versions
            .MapPost("/{id:int}/restore", RestoreOver)
            .RequireRight(Rights.DeleteFiles, Rights.DeletePermanently)
            .FolderScoped("files.view at the file and the rights of saving over it")
            .WithName("RestoreFileVersion")
            .WithSummary("Puts a previous version in place of its file; the content it had becomes a version.")
            .WithDescription(RestoreDescription);
        return api;
    }

    private static async Task<Results<Ok<FileVersions>, NotFound>> List(
        [AsParameters] VersionServices services,
        string? path,
        CancellationToken cancellationToken
    )
    {
        var (versions, trash, paths, serverFiles, work, current) = services;
        if (
            !paths.TryResolve(path, out var full)
            || paths.ToRelative(full) is not { Length: > 0 } canonical
            || serverFiles.Contains(full)
        )
        {
            return TypedResults.NotFound();
        }

        var state = await current.GetAsync();
        if (state.Check(paths, canonical, Rights.ViewFiles) != PathVerdict.Allowed)
            return TypedResults.NotFound();
        var access = TextEndpoints.AccessTo(state, paths, trash, canonical);
        var writable = !FileEndpoints.ProtectedFromWrites(Path.GetDirectoryName(full)!, serverFiles, work);
        return TypedResults.Ok(
            new FileVersions(
                await versions.ListAsync(canonical, cancellationToken),
                writable && access.Overwrite && !access.Permanent,
                writable && access.SaveCopy,
                access.Download
            )
        );
    }

    private static async Task<Results<PhysicalFileHttpResult, NotFound, ForbidHttpResult>> Content(
        [AsParameters] VersionServices services,
        HttpContext context,
        int id,
        CancellationToken cancellationToken,
        bool download = false
    )
    {
        if (await FindAsync(services, id, cancellationToken) is not { } found)
            return TypedResults.NotFound();
        if (download && found.State.Check(services.Paths, found.Canonical, Rights.Download) != PathVerdict.Allowed)
            return TypedResults.Forbid();
        if (await services.Versions.ContentPathAsync(id, cancellationToken) is not { } stored)
            return TypedResults.NotFound();
        return LibraryEndpoints.ServeFile(context, stored, download);
    }

    private static async Task<Results<Ok<LibraryItem>, NotFound, ForbidHttpResult, ProblemHttpResult>> RestoreCopy(
        [AsParameters] VersionServices services,
        TagService tags,
        int id,
        CancellationToken cancellationToken
    )
    {
        if (await FindAsync(services, id, cancellationToken) is not { } found)
            return TypedResults.NotFound();
        var (versions, trash, paths, serverFiles, work, _) = services;
        var folder = Path.GetDirectoryName(found.Full)!;
        if (!TextEndpoints.AccessTo(found.State, paths, trash, found.Canonical).SaveCopy)
            return TypedResults.Forbid();
        if (FileEndpoints.ProtectedFromWrites(folder, serverFiles, work))
            return TypedResults.Problem(FileEndpoints.ReadOnlyThroughLink, statusCode: StatusCodes.Status403Forbidden);

        var actor = FileEndpoints.Actor(found.State);
        var result = await versions.RestoreAsCopyAsync(id, found.Full, found.Canonical, actor, cancellationToken);
        if (result.Status != FileOperationStatus.Done)
            return Problem(result);
        return TypedResults.Ok((await tags.AttachIndexDataAsync([result.Item!], cancellationToken))[0]);
    }

    private static async Task<Results<Ok<RestoredVersion>, NotFound, ForbidHttpResult, ProblemHttpResult>> RestoreOver(
        [AsParameters] VersionServices services,
        TagService tags,
        int id,
        CancellationToken cancellationToken
    )
    {
        if (await FindAsync(services, id, cancellationToken) is not { } found)
            return TypedResults.NotFound();
        var (versions, trash, paths, serverFiles, work, _) = services;
        var access = TextEndpoints.AccessTo(found.State, paths, trash, found.Canonical);
        if (!access.Overwrite || access.Permanent)
            return TypedResults.Forbid();
        if (
            FileEndpoints.ProtectedFromWrites(found.Full, serverFiles, work)
            || FileEndpoints.ProtectedFromWrites(Path.GetDirectoryName(found.Full)!, serverFiles, work)
        )
        {
            return TypedResults.Problem(FileEndpoints.ReadOnlyThroughLink, statusCode: StatusCodes.Status403Forbidden);
        }

        var actor = FileEndpoints.Actor(found.State);
        var result = await versions.RestoreOverAsync(id, found.Full, found.Canonical, actor, cancellationToken);
        if (result.Status != FileOperationStatus.Done)
            return Problem(result);
        var item = (await tags.AttachIndexDataAsync([result.Item!], cancellationToken))[0];
        return TypedResults.Ok(new RestoredVersion(item, result.TrashId));
    }

    /// <summary>
    /// The version <paramref name="id"/> and its file, when the caller may see the file (null otherwise: a version of
    /// a file the caller doesn't see answers as missing).
    /// </summary>
    private static async Task<FoundVersion?> FindAsync(
        VersionServices services,
        int id,
        CancellationToken cancellationToken
    )
    {
        var (_, trash, paths, serverFiles, _, current) = services;
        if (
            await trash.FindAsync(id, cancellationToken) is not { PreviousVersion: true } item
            || !paths.TryResolve(item.Path, out var full)
            || paths.ToRelative(full) is not { Length: > 0 } canonical
            || serverFiles.Contains(full)
        )
        {
            return null;
        }

        var state = await current.GetAsync();
        return state.Check(paths, canonical, Rights.ViewFiles) == PathVerdict.Allowed
            ? new FoundVersion(state, full, canonical)
            : null;
    }

    private static ProblemHttpResult Problem(FileOperationResult result) =>
        result.Status switch
        {
            FileOperationStatus.NotFound => TypedResults.Problem(
                result.Message,
                statusCode: StatusCodes.Status404NotFound
            ),
            FileOperationStatus.ScanRunning => TypedResults.Problem(
                result.Message,
                statusCode: StatusCodes.Status409Conflict,
                type: FileEndpoints.ScanRunningType
            ),
            FileOperationStatus.Exists => TypedResults.Problem(
                result.Message,
                statusCode: StatusCodes.Status409Conflict,
                type: FileEndpoints.ExistsType
            ),
            FileOperationStatus.Forbidden => TypedResults.Problem(
                result.Message,
                statusCode: StatusCodes.Status403Forbidden
            ),
            _ => TypedResults.Problem(result.Message, statusCode: StatusCodes.Status500InternalServerError),
        };
}
