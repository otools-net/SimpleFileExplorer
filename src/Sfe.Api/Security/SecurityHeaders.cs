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

    public Task InvokeAsync(HttpContext context)
    {
        var docs = context.Request.Path.StartsWithSegments("/api/docs");
        context.Response.OnStarting(() =>
        {
            var headers = context.Response.Headers;
            headers[HeaderNames.XContentTypeOptions] = "nosniff";
            headers[HeaderNames.XFrameOptions] = "DENY";
            headers["Referrer-Policy"] = "same-origin";
            if (!headers.ContainsKey(HeaderNames.ContentSecurityPolicy))
                headers[HeaderNames.ContentSecurityPolicy] = docs ? DocsPolicy : AppPolicy;
            return Task.CompletedTask;
        });
        return next(context);
    }
}
