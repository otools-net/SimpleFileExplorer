using System.Globalization;
using System.Security.Claims;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Sfe.Core.Access;

namespace Sfe.Api.Security;

/// <summary>
/// The sign-in cookie. It holds the account's local id, its security stamp and the library in use, nothing a
/// directory (LDAP...) decides: a directory account's session works like a local one, and changing the stamp (new
/// password, disabled account) ends every session at the next request.
/// </summary>
public static class SfeSession
{
    public const string CookieName = "sfe.session";

    /// <summary>The library chosen by a visitor without an account (its slug); not a secret, rights are checked anyway.</summary>
    public const string GuestTenantCookie = "sfe.guest";

    public const string StampClaim = "sfe:stamp";
    public const string TenantClaim = "sfe:tenant";

    public static int? UserId(this ClaimsPrincipal principal) =>
        principal.Identity?.IsAuthenticated == true
        && int.TryParse(principal.FindFirstValue(ClaimTypes.NameIdentifier), NumberStyles.None, CultureInfo.InvariantCulture, out var id)
            ? id
            : null;

    public static int? TenantId(this ClaimsPrincipal principal) =>
        int.TryParse(principal.FindFirstValue(TenantClaim), NumberStyles.None, CultureInfo.InvariantCulture, out var id) ? id : null;

    /// <summary>Writes the cookie, and makes the rest of the request see the new session.</summary>
    public static async Task SignInAsync(HttpContext context, SessionUser user, int? tenantId, bool persistent)
    {
        List<Claim> claims =
        [
            new(ClaimTypes.NameIdentifier, user.Id.ToString(CultureInfo.InvariantCulture)),
            new(ClaimTypes.Name, user.UserName),
            new(StampClaim, user.SecurityStamp),
        ];
        if (tenantId is { } tenant)
            claims.Add(new Claim(TenantClaim, tenant.ToString(CultureInfo.InvariantCulture)));

        var principal = new ClaimsPrincipal(new ClaimsIdentity(claims, CookieAuthenticationDefaults.AuthenticationScheme));
        await context.SignInAsync(CookieAuthenticationDefaults.AuthenticationScheme, principal, new AuthenticationProperties { IsPersistent = persistent });
        context.User = principal;
        context.RequestServices.GetRequiredService<CurrentAccess>().Reset();
    }

    /// <summary>Removes the cookie, and makes the rest of the request see a visitor without an account.</summary>
    public static async Task SignOutAsync(HttpContext context)
    {
        await context.SignOutAsync(CookieAuthenticationDefaults.AuthenticationScheme);
        context.User = new ClaimsPrincipal(new ClaimsIdentity());
        context.RequestServices.GetRequiredService<CurrentAccess>().Reset();
    }

    /// <summary>
    /// Signs the same account in again with another library or stamp, keeping whether the cookie outlives the browser.
    /// </summary>
    public static async Task RenewAsync(HttpContext context, SessionUser user, int? tenantId)
    {
        var current = await context.AuthenticateAsync(CookieAuthenticationDefaults.AuthenticationScheme);
        await SignInAsync(context, user, tenantId, current.Properties?.IsPersistent ?? false);
    }

    /// <summary>Ends the session of an account disabled since, or whose password or status changed.</summary>
    public static async Task ValidateAsync(CookieValidatePrincipalContext context)
    {
        var principal = context.Principal;
        if (principal?.UserId() is not { } userId)
            return;
        // An install waiting for its update may lack what the accounts' query reads: the session is checked once it is done
        // (nothing but the update page answers meanwhile, and it needs no session).
        if (context.HttpContext.RequestServices.GetRequiredService<Maintenance.MigrationCoordinator>().IsBlocking)
            return;

        var accounts = context.HttpContext.RequestServices.GetRequiredService<AccessService>();
        var user = await accounts.GetSessionUserAsync(userId, context.HttpContext.RequestAborted);
        if (user is null || user.Disabled || user.SecurityStamp != principal.FindFirstValue(StampClaim))
        {
            context.RejectPrincipal();
            await context.HttpContext.SignOutAsync(CookieAuthenticationDefaults.AuthenticationScheme);
        }
    }
}
