using Microsoft.AspNetCore.Authentication.Cookies;

namespace Sfe.Api.Security;

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
        if (
            !context.HttpContext.Items.ContainsKey(SharedRead)
            || context.Options.CookieManager is not InstanceCookieManager cookies
        )
        {
            return;
        }

        context.ShouldRenew = true;
        // Where the handler wrote it: the virtual directory's path.
        var options = context.Options.Cookie.Build(context.HttpContext);
        options.Path = context.HttpContext.Request.PathBase.HasValue ? context.HttpContext.Request.PathBase.Value : "/";
        cookies._inner.DeleteCookie(context.HttpContext, context.Options.Cookie.Name!, options);
    }
}
