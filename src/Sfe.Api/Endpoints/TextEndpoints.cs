using System.Text.RegularExpressions;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.Net.Http.Headers;
using Sfe.Api.Security;
using Sfe.Core.Access;
using Sfe.Core.Files;
using Sfe.Core.Library;
using Sfe.Core.Paths;
using Sfe.Core.Settings;
using Sfe.Core.Tagging;

namespace Sfe.Api.Endpoints;

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

    public const string TooLargeType = ProblemTypes.TooLarge;
    public const string NotTextType = ProblemTypes.NotText;
    public const string ChangedType = ProblemTypes.FileChanged;
    public const string VersionRequiredType = ProblemTypes.VersionRequired;
    public const string NotEncodableType = ProblemTypes.NotEncodable;
    public const string IsLinkType = ProblemTypes.FileIsLink;
    public const string ReadOnlyType = ProblemTypes.ReadOnly;

    /// <summary>The usual and the largest part of a file read in parts (large files, logs followed).</summary>
    public const int PartBytes = 256 * 1024;
    public const int MaxPartBytes = 1024 * 1024;

    [GeneratedRegex("^[A-Za-z0-9-]{1,64}$")]
    internal static partial Regex SessionPattern();

    private static readonly string GetTextFileDescription = $"""
        At most {MaxBytes / (1024 * 1024)} MB: larger files answer 413 (`{TooLargeType}`), files that aren't
        text 415 (`{NotTextType}`). A byte order mark gives the encoding; otherwise valid UTF-8 is UTF-8 and
        anything else windows-1252; `encoding` (`utf-8`, `windows-1252`, `utf-16le`, `utf-16be`) reads it
        otherwise (Reopen with encoding). `access` says whether the caller may save over it, save a copy or
        download it.
        """;

    private static readonly string GetTextPartDescription = $"""
        About `length` bytes (default {PartBytes / 1024} KB, at most {MaxPartBytes / 1024} KB) from `offset`, or
        the last ones without an offset (`offset=-1`): cut after the first and the last line break, so that
        `end` is where the next part starts. Decoded as the whole file would be, or in `encoding`. 415
        (`{NotTextType}`) for a binary file.
        """;

    private static readonly string GetTextVersionDescription = $"""
        Read only when its size or time differ from those of `known` (the version the viewer has). Files over
        {MaxBytes / (1024 * 1024)} MB have no version (null): only their size and time.
        """;

    private const string GetSubtitleVideoDescription = """
        `Film.en.forced.srt` belongs to `Film.en.forced`, `Film.en` or `Film` with one of the library's video
        extensions, in that order. `path` is null when there is none, or none the caller may see. A few file
        checks, not a listing of the folder.
        """;

    private static readonly string SaveTextFileDescription = $"""
        Needs `If-Match` with the version the editor started from (`*`: whatever is there now): 428
        (`{VersionRequiredType}`) without it, 412 (`{ChangedType}`) when the file changed meanwhile. The text is
        written in `encoding` with `lineEnding` (`lf`, `crlf`, `cr`); characters the encoding can't hold answer
        422 (`{NotEncodableType}`, with `characters`). The previous version goes to the trash at the first save
        of an editing `session` (not in a space that always deletes for good); the file keeps its tags. 409 for
        a link (`{IsLinkType}`), a read-only file (`{ReadOnlyType}`) or a scan running
        (`{FileEndpoints.ScanRunningType}`). Answers the new version (also the ETag).
        """;

    private static readonly string SaveTextCopyDescription = $"""
        Creates the file `name` in the folder `path` with the text in `encoding` and `lineEnding`, never over
        anything (409 `{FileEndpoints.ExistsType}`, even for an item the caller doesn't see). Answers the new
        file as listings show it; 400 for a name that can't be used, 422 (`{NotEncodableType}`) as when saving.
        """;

    public static RouteGroupBuilder MapTextEndpoints(this RouteGroupBuilder api)
    {
        var text = api.MapGroup("/files/text").WithTags("Files");

        text.MapGet("", Get)
            .RequireRight(Rights.ViewFiles)
            .FolderScoped("files.view at the file; access says what saving it would need")
            .WithName("GetTextFile")
            .WithSummary(
                "A file's content as text, with its encoding, line ending, version and what the caller may do with it."
            )
            .WithDescription(GetTextFileDescription);
        text.MapGet("/part", GetPart)
            .RequireRight(Rights.ViewFiles)
            .FolderScoped("files.view at the file")
            .WithName("GetTextPart")
            .WithSummary("A part of a text file of any size, at whole lines: large files and logs.")
            .WithDescription(GetTextPartDescription);
        text.MapGet("/version", GetVersion)
            .RequireRight(Rights.ViewFiles)
            .FolderScoped("files.view at the file")
            .WithName("GetTextVersion")
            .WithSummary("The version of a text file now: has it changed since it was opened?")
            .WithDescription(GetTextVersionDescription);
        text.MapGet("/video", GetVideo)
            .RequireRight(Rights.ViewFiles)
            .FolderScoped("files.view at the subtitle file and at the video")
            .WithName("GetSubtitleVideo")
            .WithSummary("The video a subtitle file belongs to: next to it, named as it is without its language marks.")
            .WithDescription(GetSubtitleVideoDescription);
        text.MapPut("", Save)
            .RequireRight(Rights.DeleteFiles, Rights.DeletePermanently)
            .FolderScoped(
                "files.view at the file, files.delete (files.deletePermanently in a space that always deletes for "
                    + "good) at it and its folder, files.createFiles at its folder"
            )
            .WithName("SaveTextFile")
            .WithSummary("Saves text over a file, as an OS text editor does.")
            .WithDescription(SaveTextFileDescription);
        text.MapPost("", SaveCopy)
            .RequireRight(Rights.CreateFiles)
            .FolderScoped("files.createFiles at the folder the copy goes in")
            .WithName("SaveTextCopy")
            .WithSummary("Saves text as a new file.")
            .WithDescription(SaveTextCopyDescription);
        return api;
    }

    private static async Task<Results<Ok<TextFile>, NotFound, ForbidHttpResult, ProblemHttpResult>> Get(
        [AsParameters] TextServices services,
        string? path,
        string? encoding,
        CancellationToken cancellationToken
    )
    {
        var (content, trash, paths, serverFiles, work, current, context, _) = services;
        if (encoding is not null && !TextCodec.ReadEncodings.Contains(encoding))
            return UnknownEncoding();
        var (full, canonical, state, refused) = await ResolveAsync(paths, serverFiles, work, current, path);
        if (refused is { } forbidden)
            return forbidden ? TypedResults.Forbid() : TypedResults.NotFound();

        FileRead? read;
        try
        {
            read = await content.ReadAsync(full, MaxBytes, cancellationToken);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            // Never the server's message: it names the full path.
            return TypedResults.Problem(
                "The file can't be read now (in use, or the server may not read it).",
                statusCode: StatusCodes.Status500InternalServerError
            );
        }
        if (read is null)
            return TypedResults.NotFound();
        if (read.TooLarge)
        {
            return TypedResults.Problem(
                $"It is too large to be shown as text (more than {MaxBytes / (1024 * 1024)} MB): download it instead.",
                statusCode: StatusCodes.Status413PayloadTooLarge,
                type: TooLargeType
            );
        }

        if (TextCodec.Decode(read.Bytes, encoding) is not { } decoded)
        {
            return TypedResults.Problem(
                "It is not a text file.",
                statusCode: StatusCodes.Status415UnsupportedMediaType,
                type: NotTextType
            );
        }

        var version = read.Version!;
        context.Response.Headers.CacheControl = "no-store";
        context.Response.Headers.ETag = new EntityTagHeaderValue($"\"{version.Token}\"").ToString();
        return TypedResults.Ok(
            new TextFile(
                decoded.Text,
                decoded.Encoding,
                decoded.LineEnding,
                version.Token,
                version.Size,
                version.Modified,
                AccessTo(state, paths, trash, canonical),
                decoded.LineEnding == "mixed" ? TextCodec.DominantLineEnding(decoded.Text) : decoded.LineEnding
            )
        );
    }

    private static async Task<Results<Ok<TextPart>, NotFound, ForbidHttpResult, ProblemHttpResult>> GetPart(
        [AsParameters] TextServices services,
        [AsParameters] TextPartQuery query,
        CancellationToken cancellationToken
    )
    {
        var (content, _, paths, serverFiles, work, current, context, _) = services;
        var (path, offset, length, encoding) = query;
        if (encoding is not null && !TextCodec.ReadEncodings.Contains(encoding))
            return UnknownEncoding();
        if (length is < 1 or > MaxPartBytes)
        {
            return TypedResults.Problem(
                $"A part is 1 to {MaxPartBytes} bytes long.",
                statusCode: StatusCodes.Status400BadRequest
            );
        }

        var (full, _, _, refused) = await ResolveAsync(paths, serverFiles, work, current, path);
        if (refused is { } forbidden)
            return forbidden ? TypedResults.Forbid() : TypedResults.NotFound();
        TextPart? part;
        try
        {
            part = await content.ReadPartAsync(full, offset ?? -1, length ?? PartBytes, encoding, cancellationToken);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return TypedResults.Problem(
                "The file can't be read now (in use, or the server may not read it).",
                statusCode: StatusCodes.Status500InternalServerError
            );
        }
        if (part is null)
            return TypedResults.NotFound();
        if (part.Text is null)
        {
            return TypedResults.Problem(
                "It is not a text file.",
                statusCode: StatusCodes.Status415UnsupportedMediaType,
                type: NotTextType
            );
        }

        context.Response.Headers.CacheControl = "no-store";
        return TypedResults.Ok(part);
    }

    private static async Task<Results<Ok<TextVersion>, NotFound, ForbidHttpResult, ProblemHttpResult>> GetVersion(
        [AsParameters] TextServices services,
        string? path,
        string? known,
        CancellationToken cancellationToken
    )
    {
        var (content, _, paths, serverFiles, work, current, context, _) = services;
        var (full, _, _, refused) = await ResolveAsync(paths, serverFiles, work, current, path);
        if (refused is { } forbidden)
            return forbidden ? TypedResults.Forbid() : TypedResults.NotFound();
        context.Response.Headers.CacheControl = "no-store";
        var info = new FileInfo(full);
        if (info.Length > MaxBytes)
            return TypedResults.Ok(new TextVersion(null, info.Length, info.LastWriteTimeUtc));
        try
        {
            return await content.VersionAsync(full, known, cancellationToken) is { } version
                ? TypedResults.Ok(new TextVersion(version.Token, version.Size, version.Modified))
                : TypedResults.NotFound();
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return TypedResults.Problem(
                "The file can't be read now (in use, or the server may not read it).",
                statusCode: StatusCodes.Status500InternalServerError
            );
        }
    }

    private static async Task<Results<Ok<SubtitleVideo>, NotFound, ForbidHttpResult>> GetVideo(
        SettingsService settings,
        [AsParameters] TextServices services,
        string? path,
        CancellationToken cancellationToken
    )
    {
        var (_, _, paths, serverFiles, work, current, context, _) = services;
        var (full, _, state, refused) = await ResolveAsync(paths, serverFiles, work, current, path);
        if (refused is { } forbidden)
            return forbidden ? TypedResults.Forbid() : TypedResults.NotFound();
        context.Response.Headers.CacheControl = "no-store";
        var effective = await settings.GetEffectiveAsync(state.Tenant?.Id, state.User?.Id, cancellationToken);
        var video = Subtitles.FindVideo(full, effective.FileTypes.Videos);
        var relative =
            video is null || FileEndpoints.Protected(video, serverFiles, work) ? null : paths.ToRelative(video);
        if (relative is null || state.Check(paths, relative, Rights.ViewFiles) != PathVerdict.Allowed)
            return TypedResults.Ok(new SubtitleVideo(null));
        return TypedResults.Ok(new SubtitleVideo(relative));
    }

    /// <summary>
    /// The file <paramref name="path"/> that the caller may open (<c>files.view</c>): its full and canonical paths, or
    /// why not (<c>Refused</c>: false for 404, what isn't there or a protected or hidden place; true for 403, no
    /// right).
    /// </summary>
    private static async Task<(string Full, string Canonical, AccessState State, bool? Refused)> ResolveAsync(
        LibraryPaths paths,
        ServerFiles serverFiles,
        WorkFolder work,
        CurrentAccess current,
        string? path
    )
    {
        var state = await current.GetAsync();
        if (
            !paths.TryResolve(path, out var full)
            || paths.ToRelative(full) is not { Length: > 0 } canonical
            || !File.Exists(full)
            || FileEndpoints.Protected(full, serverFiles, work)
        )
        {
            return (string.Empty, string.Empty, state, false);
        }

        if (state.Check(paths, canonical, Rights.ViewFiles) is var verdict and not PathVerdict.Allowed)
            return (full, canonical, state, verdict != PathVerdict.NotFound);
        return (full, canonical, state, null);
    }

    private static ProblemHttpResult UnknownEncoding() =>
        TypedResults.Problem(
            $"Read it as one of: {string.Join(", ", TextCodec.ReadEncodings)}.",
            statusCode: StatusCodes.Status400BadRequest
        );

    private static async Task<
        Results<Ok<SavedText>, NotFound, ForbidHttpResult, ValidationProblem, ProblemHttpResult>
    > Save(
        [AsParameters] TextServices services,
        string? path,
        SaveTextRequest request,
        CancellationToken cancellationToken
    )
    {
        var (content, trash, paths, serverFiles, work, current, context, loggers) = services;
        var state = await current.GetAsync();
        if (
            !paths.TryResolve(path, out var full)
            || paths.ToRelative(full) is not { Length: > 0 } canonical
            || !File.Exists(full)
        )
        {
            return TypedResults.NotFound();
        }

        if (
            FileEndpoints.ProtectedFromWrites(full, serverFiles, work)
            || FileEndpoints.ProtectedFromWrites(Path.GetDirectoryName(full)!, serverFiles, work)
        )
        {
            FileEndpoints.Refused(
                PathVerdict.NotFound,
                new(state, path, "save over"),
                paths,
                loggers,
                protectedPlace: true
            );
            return TypedResults.NotFound();
        }
        if (state.Check(paths, canonical, Rights.ViewFiles) is var verdict and not PathVerdict.Allowed)
            return verdict == PathVerdict.NotFound ? TypedResults.NotFound() : TypedResults.Forbid();
        if (!AccessTo(state, paths, trash, canonical).Overwrite)
        {
            return TypedResults.Problem(
                "You may not save over this file: save a copy instead.",
                statusCode: StatusCodes.Status403Forbidden
            );
        }

        var ifMatch = IfMatch(context.Request);
        if (ifMatch is null)
        {
            return TypedResults.Problem(
                "Say which version of the file is being saved over (If-Match).",
                statusCode: StatusCodes.Status428PreconditionRequired,
                type: VersionRequiredType
            );
        }

        if (request.Session is not { } session || !SessionPattern().IsMatch(session))
        {
            return TypedResults.ValidationProblem(
                new Dictionary<string, string[]>
                {
                    ["session"] = ["An editing session (letters, digits and dashes) is needed."],
                }
            );
        }

        if (Encode(request.Text, request.Encoding, request.LineEnding) is not { } bytes)
            return EncodingProblem(request.Text, request.Encoding, request.LineEnding);
        if (bytes.Length > MaxBytes)
        {
            return TypedResults.Problem(
                $"The text is too large to be saved here (more than {MaxBytes / (1024 * 1024)} MB).",
                statusCode: StatusCodes.Status413PayloadTooLarge,
                type: TooLargeType
            );
        }

        var result = await content.OverwriteAsync(
            canonical,
            bytes,
            ifMatch,
            session,
            keepPrevious: !trash.AlwaysPermanent,
            FileEndpoints.Actor(state),
            cancellationToken
        );
        switch (result.Status)
        {
            case OverwriteStatus.Done:
                context.Response.Headers.ETag = new EntityTagHeaderValue($"\"{result.Version!.Token}\"").ToString();
                return TypedResults.Ok(
                    new SavedText(result.Version.Token, result.Version.Size, result.Version.Modified, result.TrashId)
                );
            case OverwriteStatus.NotFound:
                return TypedResults.NotFound();
            case OverwriteStatus.Changed:
                return TypedResults.Problem(
                    result.Message,
                    statusCode: StatusCodes.Status412PreconditionFailed,
                    type: ChangedType,
                    extensions: new Dictionary<string, object?> { ["version"] = result.Version?.Token }
                );
            case OverwriteStatus.IsLink:
                return TypedResults.Problem(
                    result.Message,
                    statusCode: StatusCodes.Status409Conflict,
                    type: IsLinkType
                );
            case OverwriteStatus.ReadOnly:
                return TypedResults.Problem(
                    result.Message,
                    statusCode: StatusCodes.Status409Conflict,
                    type: ReadOnlyType
                );
            case OverwriteStatus.ScanRunning:
                return TypedResults.Problem(
                    result.Message,
                    statusCode: StatusCodes.Status409Conflict,
                    type: FileEndpoints.ScanRunningType
                );
            default:
                return TypedResults.Problem(result.Message, statusCode: StatusCodes.Status500InternalServerError);
        }
    }

    private static async Task<
        Results<Ok<LibraryItem>, ValidationProblem, NotFound, ForbidHttpResult, ProblemHttpResult>
    > SaveCopy(
        FileOperations operations,
        TagService tags,
        [AsParameters] TextServices services,
        CopyTextRequest request,
        CancellationToken cancellationToken
    )
    {
        var (_, _, paths, serverFiles, work, current, _, loggers) = services;
        var state = await current.GetAsync();
        if (state.Check(paths, request.Path, Rights.CreateFiles) is var verdict and not PathVerdict.Allowed)
            return FileEndpoints.Refused(verdict, new(state, request.Path, "save a file in"), paths, loggers);
        if (
            paths.TryResolve(request.Path, out var folder)
            && FileEndpoints.ProtectedFromWrites(folder, serverFiles, work)
        )
        {
            return FileEndpoints.Refused(
                PathVerdict.NotFound,
                new(state, request.Path, "save a file in"),
                paths,
                loggers,
                protectedPlace: true
            );
        }

        if (Encode(request.Text, request.Encoding, request.LineEnding) is not { } bytes)
            return EncodingProblem(request.Text, request.Encoding, request.LineEnding);
        if (bytes.Length > MaxBytes)
        {
            return TypedResults.Problem(
                $"The text is too large to be saved here (more than {MaxBytes / (1024 * 1024)} MB).",
                statusCode: StatusCodes.Status413PayloadTooLarge,
                type: TooLargeType
            );
        }

        return await FileEndpoints.ResultAsync(
            await operations.CreateFileAsync(
                request.Path,
                request.Name,
                FileEndpoints.Actor(state),
                cancellationToken,
                bytes
            ),
            tags,
            cancellationToken
        );
    }

    /// <summary>
    /// The bytes of the text, or null when it can't be encoded as asked (see <see cref="EncodingProblem"/>).
    /// </summary>
    private static byte[]? Encode(string? text, string? encoding, string? lineEnding) =>
        text is not null
        && encoding is not null
        && lineEnding is not null
        && TextCodec.Encodings.Contains(encoding)
        && TextCodec.LineEndings.Contains(lineEnding)
            ? TextCodec.Encode(text, encoding, lineEnding).Bytes
            : null;

    private static ProblemHttpResult EncodingProblem(string? text, string? encoding, string? lineEnding)
    {
        if (
            text is null
            || encoding is null
            || lineEnding is null
            || !TextCodec.Encodings.Contains(encoding)
            || !TextCodec.LineEndings.Contains(lineEnding)
        )
        {
            return TypedResults.Problem(
                $"The text, an encoding ({string.Join(", ", TextCodec.Encodings)}) and a line ending (lf, crlf, cr) "
                    + $"are needed.",
                statusCode: StatusCodes.Status400BadRequest
            );
        }

        var missing = TextCodec.Encode(text, encoding, lineEnding).Unrepresentable;
        return TypedResults.Problem(
            $"Some characters can't be written in {encoding}: {string.Join(" ", missing)}. Save it as UTF-8 instead.",
            statusCode: StatusCodes.Status422UnprocessableEntity,
            type: NotEncodableType,
            extensions: new Dictionary<string, object?> { ["characters"] = missing }
        );
    }

    /// <summary>
    /// The version named by <c>If-Match</c> (quotes and a weak mark taken off); <c>*</c>; null without one.
    /// </summary>
    internal static string? IfMatch(HttpRequest request)
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

    internal static TextAccess AccessTo(AccessState state, LibraryPaths paths, Trash trash, string canonical)
    {
        var parent = canonical.LastIndexOf('/') is var slash and >= 0 ? canonical[..slash] : string.Empty;
        var replace = trash.AlwaysPermanent ? Rights.DeletePermanently : Rights.DeleteFiles;
        bool May(string at, string right) => state.Check(paths, at, right) == PathVerdict.Allowed;
        var saveCopy = May(parent, Rights.CreateFiles);
        return new TextAccess(
            saveCopy && May(canonical, Rights.ViewFiles) && May(canonical, replace) && May(parent, replace),
            saveCopy,
            May(canonical, Rights.Download),
            trash.AlwaysPermanent
        );
    }
}
