using System.Text.RegularExpressions;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.Net.Http.Headers;
using Sfe.Api.Security;
using Sfe.Core.Access;
using Sfe.Core.Files;
using Sfe.Core.Library;
using Sfe.Core.Paths;
using Sfe.Core.Tagging;

namespace Sfe.Api.Endpoints;

/// <summary>What the caller may do with a text file (from the server: the client's own idea is optimistic in searches).</summary>
/// <param name="Overwrite">Save over it: may delete it (to the trash, or for good in a space that always deletes for
/// good) and create files in its folder, as move/copy's Replace.</param>
/// <param name="SaveCopy">Save a copy in its folder (<c>files.createFiles</c> there).</param>
/// <param name="Download">Download the edited text (<c>files.download</c>).</param>
/// <param name="Permanent">The space deletes for good: no previous version is kept in the trash.</param>
public sealed record TextAccess(bool Overwrite, bool SaveCopy, bool Download, bool Permanent);

/// <param name="Text">The content, line endings as they are in the file.</param>
/// <param name="Encoding">How it was read: <c>utf-8</c>, <c>utf-8-bom</c>, <c>utf-16le</c>, <c>utf-16be</c>, <c>windows-1252</c>.</param>
/// <param name="LineEnding"><c>lf</c>, <c>crlf</c>, <c>cr</c> or <c>mixed</c>.</param>
/// <param name="Version">The content's version (also the ETag): what a save will say it changes.</param>
/// <param name="SaveLineEnding">What a save writes: the line ending, or the most used one of a file that mixes them.</param>
public sealed record TextFile(string Text, string Encoding, string LineEnding, string Version, long Size, DateTime Modified,
    TextAccess? Access = null, string? SaveLineEnding = null);

/// <param name="Session">The editing session (letters, digits, dashes; made by the client when the editor opens): the
/// previous version is kept in the trash at its first save only.</param>
public sealed record SaveTextRequest(string? Text, string? Encoding, string? LineEnding, string? Session);

/// <param name="TrashId">The previous version's trash entry, when this save kept one.</param>
public sealed record SavedText(string Version, long Size, DateTime Modified, int? TrashId);

/// <param name="Path">The folder the copy goes in.</param>
public sealed record CopyTextRequest(string? Path, string? Name, string? Text, string? Encoding, string? LineEnding);

/// <summary>
/// Text files for the viewer and editor of the web client (agents/docs/features/text-viewer.md,
/// text-editor.md): read whole, decoded as an OS text editor would; saved over (as Notepad, with the rights of
/// move/copy's Replace, the previous version kept in the trash) or as a copy. The bytes stay the server's: the client
/// never runs a library file on its origin.
/// </summary>
public static partial class TextEndpoints
{
    /// <summary>The largest file read or saved as text (larger ones are downloaded).</summary>
    public const long MaxBytes = 10 * 1024 * 1024;

    public const string TooLargeType = "too-large";
    public const string NotTextType = "not-text";
    public const string ChangedType = "file-changed";
    public const string VersionRequiredType = "version-required";
    public const string NotEncodableType = "not-encodable";
    public const string IsLinkType = "file-is-link";
    public const string ReadOnlyType = "read-only";

    [GeneratedRegex("^[A-Za-z0-9-]{1,64}$")]
    private static partial Regex SessionPattern();

    public static RouteGroupBuilder MapTextEndpoints(this RouteGroupBuilder api)
    {
        var text = api.MapGroup("/files/text").WithTags("Files");

        text.MapGet("", Get)
            .RequireRight(Rights.ViewFiles)
            .FolderScoped("files.view at the file; access says what saving it would need")
            .WithName("GetTextFile")
            .WithSummary("A file's content as text, with its encoding, line ending, version and what the caller may do with it.")
            .WithDescription($"""
                At most {MaxBytes / (1024 * 1024)} MB: larger files answer 413 (`{TooLargeType}`), files that aren't text 415
                (`{NotTextType}`). A byte order mark gives the encoding; otherwise valid UTF-8 is UTF-8 and anything else
                windows-1252. `access` says whether the caller may save over it, save a copy or download it.
                """);
        text.MapPut("", Save)
            .RequireRight(Rights.DeleteFiles, Rights.DeletePermanently)
            .FolderScoped("files.view at the file, files.delete (files.deletePermanently in a space that always deletes for good) at it and its folder, files.createFiles at its folder")
            .WithName("SaveTextFile")
            .WithSummary("Saves text over a file, as an OS text editor does.")
            .WithDescription($"""
                Needs `If-Match` with the version the editor started from (`*`: whatever is there now): 428
                (`{VersionRequiredType}`) without it, 412 (`{ChangedType}`) when the file changed meanwhile. The text is
                written in `encoding` with `lineEnding` (`lf`, `crlf`, `cr`); characters the encoding can't hold answer 422
                (`{NotEncodableType}`, with `characters`). The previous version goes to the trash at the first save of an
                editing `session` (not in a space that always deletes for good); the file keeps its tags. 409 for a link
                (`{IsLinkType}`), a read-only file (`{ReadOnlyType}`) or a scan running (`{FileEndpoints.ScanRunningType}`).
                Answers the new version (also the ETag).
                """);
        text.MapPost("", SaveCopy)
            .RequireRight(Rights.CreateFiles)
            .FolderScoped("files.createFiles at the folder the copy goes in")
            .WithName("SaveTextCopy")
            .WithSummary("Saves text as a new file.")
            .WithDescription($"""
                Creates the file `name` in the folder `path` with the text in `encoding` and `lineEnding`, never over
                anything (409 `{FileEndpoints.ExistsType}`, even for an item the caller doesn't see). Answers the new file as
                listings show it; 400 for a name that can't be used, 422 (`{NotEncodableType}`) as when saving.
                """);
        return api;
    }

    private static async Task<Results<Ok<TextFile>, NotFound, ForbidHttpResult, ProblemHttpResult>> Get(
        FileContent content, Trash trash, LibraryPaths paths, ServerFiles serverFiles, WorkFolder work, CurrentAccess current, HttpContext context,
        string? path, CancellationToken cancellationToken)
    {
        if (!paths.TryResolve(path, out var full) || paths.ToRelative(full) is not { Length: > 0 } canonical
            || !File.Exists(full) || FileEndpoints.Protected(full, serverFiles, work))
            return TypedResults.NotFound();
        var state = await current.GetAsync();
        if (state.Check(paths, canonical, Rights.ViewFiles) is var verdict and not PathVerdict.Allowed)
            return verdict == PathVerdict.NotFound ? TypedResults.NotFound() : TypedResults.Forbid();

        FileRead? read;
        try
        {
            read = await content.ReadAsync(full, MaxBytes, cancellationToken);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            // Never the server's message: it names the full path.
            return TypedResults.Problem("The file can't be read now (in use, or the server may not read it).", statusCode: StatusCodes.Status500InternalServerError);
        }
        if (read is null)
            return TypedResults.NotFound();
        if (read.TooLarge)
            return TypedResults.Problem($"It is too large to be shown as text (more than {MaxBytes / (1024 * 1024)} MB): download it instead.",
                statusCode: StatusCodes.Status413PayloadTooLarge, type: TooLargeType);
        if (TextCodec.Decode(read.Bytes) is not { } decoded)
            return TypedResults.Problem("It is not a text file.", statusCode: StatusCodes.Status415UnsupportedMediaType, type: NotTextType);

        var version = read.Version!;
        context.Response.Headers.CacheControl = "no-store";
        context.Response.Headers.ETag = new EntityTagHeaderValue($"\"{version.Token}\"").ToString();
        return TypedResults.Ok(new TextFile(decoded.Text, decoded.Encoding, decoded.LineEnding, version.Token, version.Size, version.Modified,
            AccessTo(state, paths, trash, canonical),
            decoded.LineEnding == "mixed" ? TextCodec.DominantLineEnding(decoded.Text) : decoded.LineEnding));
    }

    private static async Task<Results<Ok<SavedText>, NotFound, ForbidHttpResult, ValidationProblem, ProblemHttpResult>> Save(
        FileContent content, Trash trash, LibraryPaths paths, ServerFiles serverFiles, WorkFolder work, CurrentAccess current, HttpContext context,
        ILoggerFactory loggers, string? path, SaveTextRequest request, CancellationToken cancellationToken)
    {
        var state = await current.GetAsync();
        if (!paths.TryResolve(path, out var full) || paths.ToRelative(full) is not { Length: > 0 } canonical || !File.Exists(full))
            return TypedResults.NotFound();
        if (FileEndpoints.Protected(full, serverFiles, work) || FileEndpoints.Protected(Path.GetDirectoryName(full)!, serverFiles, work))
        {
            FileEndpoints.Refused(PathVerdict.NotFound, state, paths, path, "save over", loggers, protectedPlace: true);
            return TypedResults.NotFound();
        }
        if (state.Check(paths, canonical, Rights.ViewFiles) is var verdict and not PathVerdict.Allowed)
            return verdict == PathVerdict.NotFound ? TypedResults.NotFound() : TypedResults.Forbid();
        if (!AccessTo(state, paths, trash, canonical).Overwrite)
            return TypedResults.Problem("You may not save over this file: save a copy instead.", statusCode: StatusCodes.Status403Forbidden);

        var ifMatch = IfMatch(context.Request);
        if (ifMatch is null)
            return TypedResults.Problem("Say which version of the file is being saved over (If-Match).", statusCode: StatusCodes.Status428PreconditionRequired,
                type: VersionRequiredType);
        if (request.Session is not { } session || !SessionPattern().IsMatch(session))
            return TypedResults.ValidationProblem(new Dictionary<string, string[]> { ["session"] = ["An editing session (letters, digits and dashes) is needed."] });
        if (Encode(request.Text, request.Encoding, request.LineEnding) is not { } bytes)
            return EncodingProblem(request.Text, request.Encoding, request.LineEnding);
        if (bytes.Length > MaxBytes)
            return TypedResults.Problem($"The text is too large to be saved here (more than {MaxBytes / (1024 * 1024)} MB).",
                statusCode: StatusCodes.Status413PayloadTooLarge, type: TooLargeType);

        var result = await content.OverwriteAsync(canonical, bytes, ifMatch, session, keepPrevious: !trash.AlwaysPermanent, FileEndpoints.Actor(state), cancellationToken);
        switch (result.Status)
        {
            case OverwriteStatus.Done:
                context.Response.Headers.ETag = new EntityTagHeaderValue($"\"{result.Version!.Token}\"").ToString();
                return TypedResults.Ok(new SavedText(result.Version.Token, result.Version.Size, result.Version.Modified, result.TrashId));
            case OverwriteStatus.NotFound:
                return TypedResults.NotFound();
            case OverwriteStatus.Changed:
                return TypedResults.Problem(result.Message, statusCode: StatusCodes.Status412PreconditionFailed, type: ChangedType,
                    extensions: new Dictionary<string, object?> { ["version"] = result.Version?.Token });
            case OverwriteStatus.IsLink:
                return TypedResults.Problem(result.Message, statusCode: StatusCodes.Status409Conflict, type: IsLinkType);
            case OverwriteStatus.ReadOnly:
                return TypedResults.Problem(result.Message, statusCode: StatusCodes.Status409Conflict, type: ReadOnlyType);
            case OverwriteStatus.ScanRunning:
                return TypedResults.Problem(result.Message, statusCode: StatusCodes.Status409Conflict, type: FileEndpoints.ScanRunningType);
            default:
                return TypedResults.Problem(result.Message, statusCode: StatusCodes.Status500InternalServerError);
        }
    }

    private static async Task<Results<Ok<LibraryItem>, ValidationProblem, NotFound, ForbidHttpResult, ProblemHttpResult>> SaveCopy(
        FileOperations operations, TagService tags, LibraryPaths paths, ServerFiles serverFiles, WorkFolder work, CurrentAccess current,
        ILoggerFactory loggers, CopyTextRequest request, CancellationToken cancellationToken)
    {
        var state = await current.GetAsync();
        if (state.Check(paths, request.Path, Rights.CreateFiles) is var verdict and not PathVerdict.Allowed)
            return FileEndpoints.Refused(verdict, state, paths, request.Path, "save a file in", loggers);
        if (paths.TryResolve(request.Path, out var folder) && FileEndpoints.Protected(folder, serverFiles, work))
            return FileEndpoints.Refused(PathVerdict.NotFound, state, paths, request.Path, "save a file in", loggers, protectedPlace: true);
        if (Encode(request.Text, request.Encoding, request.LineEnding) is not { } bytes)
            return EncodingProblem(request.Text, request.Encoding, request.LineEnding);
        if (bytes.Length > MaxBytes)
            return TypedResults.Problem($"The text is too large to be saved here (more than {MaxBytes / (1024 * 1024)} MB).",
                statusCode: StatusCodes.Status413PayloadTooLarge, type: TooLargeType);

        return await FileEndpoints.ResultAsync(
            await operations.CreateFileAsync(request.Path, request.Name, FileEndpoints.Actor(state), cancellationToken, bytes), tags, cancellationToken);
    }

    /// <summary>The bytes of the text, or null when it can't be encoded as asked (see <see cref="EncodingProblem"/>).</summary>
    private static byte[]? Encode(string? text, string? encoding, string? lineEnding) =>
        text is not null && encoding is not null && lineEnding is not null
        && TextCodec.Encodings.Contains(encoding) && TextCodec.LineEndings.Contains(lineEnding)
            ? TextCodec.Encode(text, encoding, lineEnding).Bytes
            : null;

    private static ProblemHttpResult EncodingProblem(string? text, string? encoding, string? lineEnding)
    {
        if (text is null || encoding is null || lineEnding is null || !TextCodec.Encodings.Contains(encoding) || !TextCodec.LineEndings.Contains(lineEnding))
            return TypedResults.Problem($"The text, an encoding ({string.Join(", ", TextCodec.Encodings)}) and a line ending (lf, crlf, cr) are needed.",
                statusCode: StatusCodes.Status400BadRequest);
        var missing = TextCodec.Encode(text, encoding, lineEnding).Unrepresentable;
        return TypedResults.Problem($"Some characters can't be written in {encoding}: {string.Join(" ", missing)}. Save it as UTF-8 instead.",
            statusCode: StatusCodes.Status422UnprocessableEntity, type: NotEncodableType,
            extensions: new Dictionary<string, object?> { ["characters"] = missing });
    }

    /// <summary>The version named by <c>If-Match</c> (quotes and a weak mark taken off); <c>*</c>; null without one.</summary>
    private static string? IfMatch(HttpRequest request)
    {
        var value = request.Headers.IfMatch.ToString().Trim();
        if (value.Length == 0)
            return null;
        if (value == "*")
            return value;
        if (value.StartsWith("W/", StringComparison.Ordinal))
            value = value[2..];
        return value.Trim('"');
    }

    private static TextAccess AccessTo(AccessState state, LibraryPaths paths, Trash trash, string canonical)
    {
        var parent = canonical.LastIndexOf('/') is var slash and >= 0 ? canonical[..slash] : string.Empty;
        var replace = trash.AlwaysPermanent ? Rights.DeletePermanently : Rights.DeleteFiles;
        bool May(string at, string right) => state.Check(paths, at, right) == PathVerdict.Allowed;
        var saveCopy = May(parent, Rights.CreateFiles);
        return new TextAccess(
            saveCopy && May(canonical, Rights.ViewFiles) && May(canonical, replace) && May(parent, replace),
            saveCopy,
            May(canonical, Rights.Download),
            trash.AlwaysPermanent);
    }
}
