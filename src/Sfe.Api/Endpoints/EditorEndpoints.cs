using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Net.Http.Headers;
using Sfe.Api.Security;
using Sfe.Core.Access;
using Sfe.Core.Editing;
using Sfe.Core.Files;
using Sfe.Core.Paths;

namespace Sfe.Api.Endpoints;

/// <param name="Version">The picture's version (also the ETag): what saving over it will say it changes.</param>
/// <param name="Overwrite">The caller may save over it: <c>files.edit</c> at its folder and the rule of move/copy's
/// Replace, and a JPEG, PNG or WebP file (the formats the editor writes).</param>
/// <param name="Permanent">The space deletes for good: no previous version is kept in the trash.</param>
public sealed record EditedImageAccess(string Version, bool Overwrite, bool Permanent);

/// <summary>The picture saved over: its size in pixels and bytes, its new version (also the ETag) and time.</summary>
/// <param name="TrashId">The previous version's trash entry, when this save kept one.</param>
public sealed record SavedImage(string Path, int Width, int Height, long Size, string Version, DateTime Modified, int? TrashId);

public static class EditorEndpoints
{
    public static RouteGroupBuilder MapEditorEndpoints(this RouteGroupBuilder api)
    {
        api.MapPost("/editor/images", SaveImage)
            .RequireRateLimiting(RequestLimits.Upload)
            .RequireRight(Rights.EditImages)
            .FolderScoped("files.view at the original, files.edit at the folder the new file goes to")
            // Room for a large lossless picture (Kestrel's default stops at 30 MB); the service checks the size again.
            .WithMetadata(new RequestSizeLimitAttribute(EditedImages.MaxBytes + 1))
            .WithName("SaveEditedImage")
            .WithTags("Editor")
            .WithSummary("Saves an edited picture as a new file of the library; the original is never changed.")
            .WithDescription($"""
                The body is the edited picture itself, as the web client's editor encoded it: JPEG, PNG or WebP (at most
                {EditedImages.MaxBytes / 1024 / 1024} MB; the format is read from its bytes, not from `Content-Type`).
                `path` is the original. The new file goes to `Editor:OutputFolder` under the original's own folders
                (`Photos/2024/beach.jpg` → `Edited/Photos/2024/beach.jpg`), or next to the original as
                `beach (edited).jpg` when the setting is empty or the original is already in that folder. An existing
                name gets ` (2)`, ` (3)`... The file is indexed at once. Answers 201 with its path and size in pixels.
                """);
        api.MapGet("/editor/images/access", Access)
            .RequireRight(Rights.EditImages)
            .FolderScoped("files.view at the picture; overwrite says whether saving over it is allowed")
            .WithName("EditedImageAccess")
            .WithTags("Editor")
            .WithSummary("The picture's version, and whether the caller may save the edited picture over it.");
        api.MapPut("/editor/images", SaveOver)
            .RequireRateLimiting(RequestLimits.Upload)
            .RequireRight(Rights.EditImages)
            .FolderScoped("files.view at the picture, files.edit at its folder, and the rule of move/copy's Replace (files.delete or files.deletePermanently at it and its folder, files.createFiles at its folder)")
            .WithMetadata(new RequestSizeLimitAttribute(EditedImages.MaxBytes + 1))
            .WithName("SaveEditedImageOver")
            .WithTags("Editor")
            .WithSummary("Saves an edited picture over the original.")
            .WithDescription($"""
                The body is the edited picture, in the original's own format (JPEG, PNG or WebP; 400 otherwise: save a
                copy instead). Needs `If-Match` with the version the editor started from (`*`: whatever is there now):
                428 (`{TextEndpoints.VersionRequiredType}`) without it, 412 (`{TextEndpoints.ChangedType}`) when the
                picture changed meanwhile. The previous version goes to the trash at the first save of an editing
                `session` (not in a space that always deletes for good); the picture keeps its tags. 409 for a link
                (`{TextEndpoints.IsLinkType}`), a read-only file (`{TextEndpoints.ReadOnlyType}`) or a scan running
                (`{FileEndpoints.ScanRunningType}`). Answers the picture's size in pixels, its new version (also the ETag) and time.
                """);

        return api;
    }

    private static async Task<Results<Created<EditedImage>, ProblemHttpResult>> SaveImage(
        EditedImages editor, LibraryPaths paths, CurrentAccess current, HttpRequest request, CancellationToken cancellationToken, string? path = null)
    {
        var state = await current.GetAsync();
        if (state.Check(paths, path, Rights.ViewFiles) != PathVerdict.Allowed)
            return TypedResults.Problem("The original picture was not found.", statusCode: StatusCodes.Status404NotFound);
        // The new file goes to a folder of its own (Editor:OutputFolder): its files must be the subject's to change.
        if (LibraryPaths.NormalizeRelative(path) is { } source && editor.TargetFolder(source) is { } folder
            && state.Check(paths, folder, Rights.EditImages) != PathVerdict.Allowed)
            return TypedResults.Problem("You may not add files to the folder the edited picture would go to.", statusCode: StatusCodes.Status403Forbidden);

        var result = await editor.SaveAsync(path, request.Body, cancellationToken);
        return result.Outcome switch
        {
            EditedImageOutcome.Saved => TypedResults.Created((string?)null, result.Image!),
            EditedImageOutcome.SourceNotFound => TypedResults.Problem(result.Message, statusCode: StatusCodes.Status404NotFound),
            EditedImageOutcome.TooLarge => TypedResults.Problem(result.Message, statusCode: StatusCodes.Status413PayloadTooLarge),
            EditedImageOutcome.InvalidImage => TypedResults.Problem(result.Message, statusCode: StatusCodes.Status400BadRequest),
            // Not a 409: the client takes those as answers carrying the current state.
            _ => TypedResults.Problem(result.Message, statusCode: StatusCodes.Status500InternalServerError),
        };
    }

    private static async Task<Results<Ok<EditedImageAccess>, NotFound>> Access(
        FileContent content, Trash trash, LibraryPaths paths, ServerFiles serverFiles, WorkFolder work, CurrentAccess current, HttpContext context,
        string? path, CancellationToken cancellationToken)
    {
        var state = await current.GetAsync();
        if (Picture(paths, serverFiles, work, path) is not (var full, var canonical) || state.Check(paths, canonical, Rights.ViewFiles) != PathVerdict.Allowed
            || await content.VersionAsync(full, null, cancellationToken) is not { } version)
            return TypedResults.NotFound();
        context.Response.Headers.CacheControl = "no-store";
        context.Response.Headers.ETag = new EntityTagHeaderValue($"\"{version.Token}\"").ToString();
        return TypedResults.Ok(new EditedImageAccess(version.Token, MayOverwrite(state, paths, trash, canonical), trash.AlwaysPermanent));
    }

    private static async Task<Results<Ok<SavedImage>, NotFound, ValidationProblem, ProblemHttpResult>> SaveOver(
        EditedImages editor, Trash trash, LibraryPaths paths, ServerFiles serverFiles, WorkFolder work, CurrentAccess current, HttpContext context,
        string? path, string? session, CancellationToken cancellationToken)
    {
        var state = await current.GetAsync();
        if (Picture(paths, serverFiles, work, path) is not (_, var canonical) || state.Check(paths, canonical, Rights.ViewFiles) != PathVerdict.Allowed)
            return TypedResults.NotFound();
        if (!MayOverwrite(state, paths, trash, canonical))
            return TypedResults.Problem("You may not save over this picture: save a copy instead.", statusCode: StatusCodes.Status403Forbidden);
        if (TextEndpoints.IfMatch(context.Request) is not { } ifMatch)
            return TypedResults.Problem("Say which version of the picture is being saved over (If-Match).", statusCode: StatusCodes.Status428PreconditionRequired,
                type: TextEndpoints.VersionRequiredType);
        if (session is null || !TextEndpoints.SessionPattern().IsMatch(session))
            return TypedResults.ValidationProblem(new Dictionary<string, string[]> { ["session"] = ["An editing session (letters, digits and dashes) is needed."] });

        var result = await editor.SaveOverAsync(canonical, context.Request.Body, ifMatch, session, keepPrevious: !trash.AlwaysPermanent,
            FileEndpoints.Actor(state), cancellationToken);
        if (result.Outcome == EditedImageOutcome.Saved)
        {
            var (image, version) = (result.Image!, result.Written!.Version!);
            context.Response.Headers.ETag = new EntityTagHeaderValue($"\"{version.Token}\"").ToString();
            return TypedResults.Ok(new SavedImage(image.Path, image.Width, image.Height, image.Size, version.Token, version.Modified, result.Written.TrashId));
        }
        return (result.Outcome, result.Written?.Status) switch
        {
            (EditedImageOutcome.SourceNotFound, _) or (_, OverwriteStatus.NotFound) => TypedResults.NotFound(),
            (EditedImageOutcome.TooLarge, _) => TypedResults.Problem(result.Message, statusCode: StatusCodes.Status413PayloadTooLarge),
            (EditedImageOutcome.InvalidImage or EditedImageOutcome.OtherFormat, _) => TypedResults.Problem(result.Message, statusCode: StatusCodes.Status400BadRequest),
            (_, OverwriteStatus.Changed) => TypedResults.Problem(result.Message, statusCode: StatusCodes.Status412PreconditionFailed, type: TextEndpoints.ChangedType,
                extensions: new Dictionary<string, object?> { ["version"] = result.Written!.Version?.Token }),
            (_, OverwriteStatus.IsLink) => TypedResults.Problem(result.Message, statusCode: StatusCodes.Status409Conflict, type: TextEndpoints.IsLinkType),
            (_, OverwriteStatus.ReadOnly) => TypedResults.Problem(result.Message, statusCode: StatusCodes.Status409Conflict, type: TextEndpoints.ReadOnlyType),
            (_, OverwriteStatus.ScanRunning) => TypedResults.Problem(result.Message, statusCode: StatusCodes.Status409Conflict, type: FileEndpoints.ScanRunningType),
            _ => TypedResults.Problem(result.Message, statusCode: StatusCodes.Status500InternalServerError),
        };
    }

    /// <summary>A file of the library (not the server's own files nor the work folder): where it is, its canonical path.</summary>
    private static (string Full, string Canonical)? Picture(LibraryPaths paths, ServerFiles serverFiles, WorkFolder work, string? path) =>
        paths.TryResolve(path, out var full) && paths.ToRelative(full) is { Length: > 0 } canonical && File.Exists(full)
        && !FileEndpoints.Protected(full, serverFiles, work) && !FileEndpoints.Protected(Path.GetDirectoryName(full)!, serverFiles, work)
            ? (full, canonical)
            : null;

    /// <summary>Saving over: a format the editor writes, files.edit at its folder, and the rule of Replace (as text files).</summary>
    private static bool MayOverwrite(AccessState state, LibraryPaths paths, Trash trash, string canonical)
    {
        var parent = canonical.LastIndexOf('/') is var slash and >= 0 ? canonical[..slash] : string.Empty;
        return EditedImages.SavesInPlace(canonical) && state.Check(paths, parent, Rights.EditImages) == PathVerdict.Allowed
            && TextEndpoints.AccessTo(state, paths, trash, canonical).Overwrite;
    }
}
