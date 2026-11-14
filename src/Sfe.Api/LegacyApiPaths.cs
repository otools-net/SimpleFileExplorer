namespace Otools.Net.Sfe.Api;

/// <summary>
/// The unversioned paths (<c>/api/tags</c>) are another name of v1 until SFE 2.0
/// (<c>agents/docs/development/api-versioning.md</c>): the request is rewritten to <c>/api/v1/tags</c> before routing
/// picks its endpoint, so it meets the same guard, rights and handler; the answer says the path is deprecated
/// (<see cref="DeprecationHeader"/>, RFC 9745) with a <c>Link</c> to the v1 path, and <see cref="LegacyPathLog"/> tells
/// the log who still calls them. The routes are mapped once (under <c>/api/v1</c>), so the OpenAPI document shows
/// only v1. The OAuth return address (<see cref="ApiRoutes.OAuthCallback"/>) is answered the same way, without the
/// deprecation: it is registered at the providers.
/// </summary>
public sealed class LegacyApiPaths(RequestDelegate next, LegacyPathLog log)
{
    public const string DeprecationHeader = "Deprecation";

    /// <summary>
    /// When the unversioned paths were deprecated (2026-11-14, the day <c>/api/v1</c> came), as RFC 9745 writes it: a
    /// structured-field date, seconds since 1970.
    /// </summary>
    public const string DeprecatedSince = "@1794614400";

    public Task InvokeAsync(HttpContext context)
    {
        var legacy = context.Request.Path;
        if (!ApiRoutes.IsLegacy(legacy))
            return next(context);

        var versioned = ApiRoutes.ToVersioned(legacy);
        context.Request.Path = versioned;
        if (ApiRoutes.IsOAuthCallback(legacy))
            return next(context);

        var successor = $"<{context.Request.PathBase.Add(versioned).ToUriComponent()}>; rel=\"successor-version\"";
        context.Response.OnStarting(() =>
        {
            context.Response.Headers[DeprecationHeader] = DeprecatedSince;
            context.Response.Headers.Append("Link", successor);
            // Once the answer starts, authentication has run: the caller is known.
            log.Note(context, legacy);
            return Task.CompletedTask;
        });
        return next(context);
    }
}
