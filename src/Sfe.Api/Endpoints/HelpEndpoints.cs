using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.AspNetCore.StaticFiles;
using Microsoft.Net.Http.Headers;
using Otools.Net.Sfe.Api.Help;
using Otools.Net.Sfe.Api.Security;
using Otools.Net.Sfe.Core.Access;

namespace Otools.Net.Sfe.Api.Endpoints;

/// <summary>
/// The help: the user documentation shipped with the app (<see cref="HelpDocs"/>), for the client's help page. No
/// right: anyone using the app may read it (a signed-in account or a visitor of a space open to guests), and anyone
/// before the first administrator exists (the setup page links to it). Read-only, the same for everyone.
/// </summary>
public static class HelpEndpoints
{
    private const string GetHelpFileDescription = """
        Markdown pages as `text/markdown`, their pictures as images; sandboxed as the library's files are. Answers
        `If-None-Match` with 304 until the app is updated (`ETag` of the build).
        """;

    private static readonly FileExtensionContentTypeProvider Types = new();

    // A tag of the build: the files change only with it.
    private static readonly EntityTagHeaderValue BuildTag = new(
        $"\"{typeof(HelpDocs).Assembly.ManifestModule.ModuleVersionId:N}\""
    );

    public static RouteGroupBuilder MapHelpEndpoints(this RouteGroupBuilder api)
    {
        var help = api.MapGroup("/help").WithTags("Help");

        help.MapGet("/", Index)
            .AllowAnonymous()
            .WithName("GetHelpIndex")
            .WithSummary("The pages of the help with their titles and headings (for its contents and its search).");

        help.MapGet("/files/{**path}", File)
            .AllowAnonymous()
            .NotLibraryPaths()
            .WithName("GetHelpFile")
            .WithSummary("A file of the help: a page (Markdown) or one of its pictures.")
            .WithDescription(GetHelpFileDescription);

        return api;
    }

    private static async Task<Results<Ok<HelpIndex>, UnauthorizedHttpResult>> Index(
        CurrentAccess current,
        SetupService setup,
        CancellationToken cancellationToken
    ) =>
        await MayReadAsync(current, setup, cancellationToken)
            ? TypedResults.Ok(HelpDocs.Index)
            : TypedResults.Unauthorized();

    private static async Task<Results<FileStreamHttpResult, NotFound, UnauthorizedHttpResult>> File(
        HttpContext context,
        CurrentAccess current,
        SetupService setup,
        string path
    )
    {
        if (!await MayReadAsync(current, setup, context.RequestAborted))
            return TypedResults.Unauthorized();
        if (ContentTypeOf(path) is not { } contentType || HelpDocs.Open(path) is not { } stream)
            return TypedResults.NotFound();

        context.Response.Headers[HeaderNames.ContentSecurityPolicy] = SecurityHeaders.FilePolicy;
        context.Response.Headers[HeaderNames.CacheControl] = "private, no-cache";
        return TypedResults.Stream(stream, contentType, entityTag: BuildTag);
    }

    /// <summary>Signed in, a visitor with rights in a space open to guests, or a server not set up yet.</summary>
    private static async Task<bool> MayReadAsync(
        CurrentAccess current,
        SetupService setup,
        CancellationToken cancellationToken
    )
    {
        var state = await current.GetAsync();
        return state.User is not null
            || state.Access.Rights.Count > 0
            || await setup.IsRequiredAsync(cancellationToken);
    }

    private static string? ContentTypeOf(string path)
    {
        if (path.EndsWith(".md", StringComparison.OrdinalIgnoreCase))
            return "text/markdown; charset=utf-8";
        return Types.TryGetContentType(path, out var type) && type.StartsWith("image/", StringComparison.Ordinal)
            ? type
            : null;
    }
}
