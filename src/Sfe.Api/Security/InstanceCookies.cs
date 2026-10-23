using Microsoft.AspNetCore.Authentication.Cookies;

namespace Sfe.Api.Security;

/// <summary>
/// Cookie names of their own for each port: browsers keep cookies per host, not per port, so two instances on one host
/// (<c>localhost:5000</c> and <c>localhost:5001</c>) would otherwise share the session cookie, each sign-in replacing
/// the other's (which the other's keys can't read: signed out). On the default port the names stay as they were.
/// </summary>
public static class InstanceCookies
{
    /// <summary><paramref name="name"/> for the port the browser asked for (its Host): <c>sfe.session-5000</c>.</summary>
    public static string Name(HttpRequest request, string name) =>
        request.Host.Port is { } port ? $"{name}-{port}" : name;
}

/// <summary>
/// The session cookie under <see cref="InstanceCookies.Name"/>. A session of before (the shared name) is still read
/// when there is none under the new one; once it proved to be this instance's (<see cref="SfeSession.ValidateAsync"/>)
/// it is written again under the new name and the old one is removed (<see cref="RenameShared"/>).
/// </summary>
public sealed class InstanceCookieManager : ICookieManager
{
    private const string SharedRead = "sfe.shared-session-read";
    private readonly ChunkingCookieManager _inner = new();

    public string? GetRequestCookie(HttpContext context, string key)
    {
        var name = InstanceCookies.Name(context.Request, key);
        var value = _inner.GetRequestCookie(context, name);
        if (value is not null || name == key)
            return value;
        value = _inner.GetRequestCookie(context, key);
        if (value is not null)
            context.Items[SharedRead] = true;
        return value;
    }

    public void AppendResponseCookie(HttpContext context, string key, string? value, CookieOptions options) =>
        _inner.AppendResponseCookie(context, InstanceCookies.Name(context.Request, key), value, options);

    public void DeleteCookie(HttpContext context, string key, CookieOptions options) =>
        _inner.DeleteCookie(context, InstanceCookies.Name(context.Request, key), options);

    /// <summary>
    /// The session read was under the shared name and is this instance's: renewed (under the new name), and the
    /// shared one removed. One of another instance can't be read here and is left to it.
    /// </summary>
    public static void RenameShared(CookieValidatePrincipalContext context)
    {
        if (!context.HttpContext.Items.ContainsKey(SharedRead) || context.Options.CookieManager is not InstanceCookieManager cookies)
            return;
        context.ShouldRenew = true;
        // Where the handler wrote it: the virtual directory's path.
        var options = context.Options.Cookie.Build(context.HttpContext);
        options.Path = context.HttpContext.Request.PathBase.HasValue ? context.HttpContext.Request.PathBase.Value : "/";
        cookies._inner.DeleteCookie(context.HttpContext, context.Options.Cookie.Name!, options);
    }
}
