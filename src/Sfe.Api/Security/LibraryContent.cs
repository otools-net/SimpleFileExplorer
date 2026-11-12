namespace Sfe.Api.Security;

/// <summary>
/// Keeps a file of the library (anyone's content) from being run by a page of the app. The app's policy allows scripts
/// and styles of its own origin, and the library's files are served from that origin: without this, whoever uploads
/// <c>x.js</c> owns a script address the policy accepts, and any markup injected into a page could run it.
/// </summary>
public static class LibraryContent
{
    private const string Download = "application/octet-stream";

    /// <summary>What a browser says it loads a script, a worker or a plug-in for (<c>Sec-Fetch-Dest</c>).</summary>
    private static readonly string[] Running =
    [
        "script",
        "worker",
        "sharedworker",
        "serviceworker",
        "audioworklet",
        "paintworklet",
        "embed",
        "object",
        "xslt",
    ];

    private static readonly string[] RunningOrStyling = [.. Running, "style"];

    /// <summary>
    /// The type a library file is served as: a script, style sheet or WebAssembly type becomes a plain download type,
    /// which the browser refuses to run or apply (every answer carries <c>nosniff</c>).
    /// </summary>
    public static string SafeType(string contentType)
    {
        var runs =
            contentType.Contains("javascript", StringComparison.OrdinalIgnoreCase)
            || contentType.Contains("ecmascript", StringComparison.OrdinalIgnoreCase)
            || contentType.Contains("jscript", StringComparison.OrdinalIgnoreCase)
            || contentType.Contains("livescript", StringComparison.OrdinalIgnoreCase)
            || contentType.Equals("application/wasm", StringComparison.OrdinalIgnoreCase)
            || contentType.Equals("text/css", StringComparison.OrdinalIgnoreCase);
        return runs ? Download : contentType;
    }

    /// <summary>
    /// Answers 404 when a page loads the route's file as a script, a worker, a plug-in or a style sheet.
    /// </summary>
    public static RouteHandlerBuilder NeverRunByPages(this RouteHandlerBuilder route) =>
        route.Refusing(RunningOrStyling);

    /// <summary>As <see cref="NeverRunByPages"/>, but style sheets load: a book's own styles (EPUB).</summary>
    public static RouteHandlerBuilder NeverRunByPagesButStyled(this RouteHandlerBuilder route) =>
        route.Refusing(Running);

    private static RouteHandlerBuilder Refusing(this RouteHandlerBuilder route, string[] destinations) =>
        route.AddEndpointFilter(
            async (invocation, next) =>
            {
                var destination = invocation.HttpContext.Request.Headers["Sec-Fetch-Dest"].ToString();
                if (destinations.Contains(destination, StringComparer.OrdinalIgnoreCase))
                    return Results.NotFound();
                return await next(invocation);
            }
        );
}
