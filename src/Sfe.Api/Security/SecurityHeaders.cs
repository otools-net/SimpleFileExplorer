using Microsoft.Net.Http.Headers;

namespace Sfe.Api.Security;

/// <summary>
/// Headers every answer carries, so that a page of another site cannot frame the app (clickjacking), the browser never
/// guesses a type, links don't tell other sites the app's addresses, and the app's pages run only the app's own scripts.
/// A route that sets its own <c>Content-Security-Policy</c> (the library's files: <see cref="FilePolicy"/>) keeps it.
/// </summary>
public sealed class SecurityHeaders(RequestDelegate next)
{
    /// <summary>
    /// The app's pages: scripts of the app only; Angular adds component styles as &lt;style&gt; elements. Pictures and
    /// videos may come from a data server (<c>Sfe:DataServer</c>, any address), which runs nothing.
    /// </summary>
    public const string AppPolicy =
        "default-src 'self'; script-src 'self'; style-src 'self' 'unsafe-inline'; img-src 'self' data: blob: http: https:; " +
        "media-src 'self' blob: http: https:; connect-src 'self'; font-src 'self' data:; object-src 'none'; base-uri 'self'; " +
        "form-action 'self'; frame-ancestors 'none'";

    /// <summary>Swagger UI (<c>api/docs</c>, which needs a right) has scripts of its own inline.</summary>
    public const string DocsPolicy =
        "default-src 'self'; script-src 'self' 'unsafe-inline'; style-src 'self' 'unsafe-inline'; img-src 'self' data:; " +
        "object-src 'none'; base-uri 'self'; frame-ancestors 'none'";

    /// <summary>
    /// A file of the library opened by itself: its content is anyone's (a page or an SVG dropped in the library), so it
    /// runs nothing and reaches nothing, as a page of no origin.
    /// </summary>
    public const string FilePolicy = "default-src 'none'; style-src 'unsafe-inline'; sandbox";

    /// <summary>The page of the Flash viewer's frame (<see cref="FlashPlayerPage"/>), the only one framed by the app.</summary>
    public const string FlashPlayerPage = "/flash/player.html";

    /// <summary>Ruffle's own files (scripts, WebAssembly): fetched by the player's frame, which has no origin.</summary>
    public const string RuffleFiles = "/flash/ruffle";

    /// <summary>
    /// The Flash viewer's frame (agents/docs/features/flash-viewer.md): Ruffle compiles WebAssembly (<c>'wasm-unsafe-eval'</c>,
    /// on this page only), and the page is sandboxed without <c>allow-same-origin</c>: it has no origin, so neither Ruffle
    /// nor a movie can send the session's cookie, read the app's storage or reach the page around it. It loads only the
    /// app's files; only the app may frame it.
    /// </summary>
    public const string FlashPolicy =
        "default-src 'none'; script-src 'self' 'wasm-unsafe-eval'; connect-src 'self'; img-src 'self' data: blob:; media-src 'self' blob:; " +
        "font-src 'self' data:; style-src 'unsafe-inline'; base-uri 'none'; form-action 'none'; frame-ancestors 'self'; sandbox allow-scripts allow-pointer-lock";

    public Task InvokeAsync(HttpContext context)
    {
        var path = context.Request.Path;
        var docs = path.StartsWithSegments("/api/docs");
        var flash = path.Equals(FlashPlayerPage, StringComparison.OrdinalIgnoreCase);
        var ruffle = path.StartsWithSegments(RuffleFiles, StringComparison.OrdinalIgnoreCase);
        context.Response.OnStarting(() =>
        {
            var headers = context.Response.Headers;
            headers[HeaderNames.XContentTypeOptions] = "nosniff";
            headers[HeaderNames.XFrameOptions] = flash ? "SAMEORIGIN" : "DENY";
            headers["Referrer-Policy"] = "same-origin";
            if (ruffle)
                headers.AccessControlAllowOrigin = "*";
            if (flash)
                headers[HeaderNames.ContentSecurityPolicy] = FlashPolicy;
            else if (!headers.ContainsKey(HeaderNames.ContentSecurityPolicy))
                headers[HeaderNames.ContentSecurityPolicy] = docs ? DocsPolicy : AppPolicy;
            return Task.CompletedTask;
        });
        return next(context);
    }
}
