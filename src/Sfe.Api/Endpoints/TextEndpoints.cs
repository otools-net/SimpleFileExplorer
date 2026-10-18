using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.Net.Http.Headers;
using Sfe.Api.Security;
using Sfe.Core.Access;
using Sfe.Core.Files;
using Sfe.Core.Paths;

namespace Sfe.Api.Endpoints;

/// <param name="Text">The content, line endings as they are in the file.</param>
/// <param name="Encoding">How it was read: <c>utf-8</c>, <c>utf-8-bom</c>, <c>utf-16le</c>, <c>utf-16be</c>, <c>windows-1252</c>.</param>
/// <param name="LineEnding"><c>lf</c>, <c>crlf</c>, <c>cr</c> or <c>mixed</c>.</param>
/// <param name="Version">The content's version (also the ETag): what a save will say it changes.</param>
public sealed record TextFile(string Text, string Encoding, string LineEnding, string Version, long Size, DateTime Modified);

/// <summary>
/// Text files for the viewer of the web client (agents/docs/features/text-viewer.md): read whole, decoded as an OS
/// text editor would. The bytes stay the server's: the client never runs a library file on its origin.
/// </summary>
public static class TextEndpoints
{
    /// <summary>The largest file read as text (larger ones are downloaded).</summary>
    public const long MaxBytes = 10 * 1024 * 1024;

    public const string TooLargeType = "too-large";
    public const string NotTextType = "not-text";

    public static RouteGroupBuilder MapTextEndpoints(this RouteGroupBuilder api)
    {
        var text = api.MapGroup("/files/text").WithTags("Files");

        text.MapGet("", Get)
            .RequireRight(Rights.ViewFiles)
            .FolderScoped("files.view at the file")
            .WithName("GetTextFile")
            .WithSummary("A file's content as text, with its encoding, line ending and version.")
            .WithDescription($"""
                At most {MaxBytes / (1024 * 1024)} MB: larger files answer 413 (`too-large`), files that aren't text 415
                (`not-text`). A byte order mark gives the encoding; otherwise valid UTF-8 is UTF-8 and anything else
                windows-1252.
                """);
        return api;
    }

    private static async Task<Results<Ok<TextFile>, NotFound, ForbidHttpResult, ProblemHttpResult>> Get(
        FileContent content, LibraryPaths paths, ServerFiles serverFiles, WorkFolder work, CurrentAccess current, HttpContext context,
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
        return TypedResults.Ok(new TextFile(decoded.Text, decoded.Encoding, decoded.LineEnding, version.Token, version.Size, version.Modified));
    }
}
