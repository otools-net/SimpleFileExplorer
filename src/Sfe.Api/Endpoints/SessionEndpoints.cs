using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.Extensions.Options;
using Sfe.Api.Security;
using Sfe.Core.Access;
using Sfe.Core.Data;
using Sfe.Core.Options;

namespace Sfe.Api.Endpoints;

/// <param name="Local">A local account: its password can be changed here (a directory account's is changed in the directory).</param>
public sealed record SessionAccount(int Id, string UserName, string? DisplayName, UserKind Kind, bool IsServerAdmin, bool Local);

public sealed record SessionTenant(int Id, string Slug, string Name);

/// <param name="User">The signed-in account; null for a visitor without one.</param>
/// <param name="Tenant">The library the session works in.</param>
/// <param name="Tenants">The libraries the account can switch to.</param>
/// <param name="GuestTenants">Libraries open to visitors without an account (listed on the sign-in page).</param>
/// <param name="SetupRequired">No administrator yet: only the setup page works.</param>
/// <param name="SetupAvailable">The setup page can be used (also to recover an administrator, <c>Sfe:Setup:Recover</c>).</param>
/// <param name="CanBrowse">The library can be browsed in this session (otherwise the client shows the sign-in page).</param>
public sealed record SessionInfo(
    SessionAccount? User, SessionTenant? Tenant, IReadOnlyList<SessionTenant> Tenants, IReadOnlyList<SessionTenant> GuestTenants,
    bool SetupRequired, bool SetupAvailable, bool CanBrowse);

public sealed record SignInRequest(string? UserName, string? Password, bool Remember = false);

/// <param name="Tenant">The library's short name (<see cref="SessionTenant.Slug"/>).</param>
public sealed record TenantChoice(string? Tenant);

public sealed record PasswordChange(string? Current, string? Password);

public static class SessionEndpoints
{
    public const string SignInRateLimit = "sign-in";

    public static RouteGroupBuilder MapSessionEndpoints(this RouteGroupBuilder api)
    {
        var session = api.MapGroup("/session").WithTags("Session");

        session.MapGet("/", async (HttpContext context, CancellationToken _) => TypedResults.Ok(await DescribeAsync(context)))
            .AllowAnonymous()
            .WithName("GetSession")
            .WithSummary("Who is signed in, in which space, and what the sign-in page offers.");

        session.MapPost("/login", SignIn)
            .AllowAnonymous()
            .RequireRateLimiting(SignInRateLimit)
            .WithName("SignIn")
            .WithSummary("Signs in with a name and password.")
            .WithDescription("`remember` keeps the session after the browser closes (for `Sfe:Security:SessionDays` without use).");

        session.MapPost("/logout", SignOut)
            .AllowAnonymous()
            .WithName("SignOut")
            .WithSummary("Signs out.");

        session.MapPost("/guest", ChooseGuestTenant)
            .AllowAnonymous()
            .WithName("BrowseAsGuest")
            .WithSummary("Picks the space a visitor without an account browses, among those open to guests (signs out first).");

        session.MapPost("/tenant", SwitchTenant)
            .AllowAnonymous()
            .WithName("SwitchTenant")
            .WithSummary("Switches the signed-in account to another of its spaces.");

        session.MapPut("/password", ChangePassword)
            .RequireRight(Rights.OwnPassword)
            .RequireRateLimiting(SignInRateLimit)
            .WithName("ChangeOwnPassword")
            .WithSummary("Changes the password of the signed-in local account; its other sessions end.");

        return api;
    }

    internal static async Task<SessionInfo> DescribeAsync(HttpContext context)
    {
        var services = context.RequestServices;
        var accounts = services.GetRequiredService<AccessService>();
        var state = await services.GetRequiredService<CurrentAccess>().GetAsync();
        var tenants = await accounts.GetCachedTenantsAsync(context.RequestAborted);
        var reachable = state.User is { } user
            ? await services.GetRequiredService<RightsEvaluator>().GetTenantIdsAsync(user.Id, context.RequestAborted)
            : [];
        var guestsAllowed = services.GetRequiredService<IOptionsMonitor<SecurityOptions>>().CurrentValue.AnonymousGuests;
        var setup = await services.GetRequiredService<SetupService>().GetStateAsync(context.RequestAborted);

        return new SessionInfo(
            state.User is { } account
                ? new SessionAccount(account.Id, account.UserName, account.DisplayName, account.Kind, account.IsServerAdmin, account.IsLocal)
                : null,
            state.Tenant is { } tenant ? ToSession(tenant) : null,
            tenants.Where(t => reachable.Contains(t.Id)).Select(ToSession).ToList(),
            state.User is null && guestsAllowed ? tenants.Where(t => t.GuestAccess && !t.Disabled).Select(ToSession).ToList() : [],
            setup.Required,
            setup.Available,
            state.Has(Rights.Browse));
    }

    private static SessionTenant ToSession(TenantInfo tenant) => new(tenant.Id, tenant.Slug, tenant.Name);

    private static async Task<Results<Ok<SessionInfo>, ProblemHttpResult>> SignIn(
        HttpContext context, SignInService signIn, SignInRequest request, CancellationToken cancellationToken)
    {
        var result = await signIn.SignInAsync(request.UserName, request.Password, cancellationToken);
        switch (result.Status)
        {
            case SignInStatus.LockedOut:
                var minutes = Math.Max(1, (int)Math.Ceiling(((result.LockedUntil ?? DateTime.UtcNow) - DateTime.UtcNow).TotalMinutes));
                return TypedResults.Problem($"Too many failed attempts: try again in {minutes} minute{(minutes == 1 ? "" : "s")}.", statusCode: StatusCodes.Status400BadRequest);
            case SignInStatus.Disabled:
                return TypedResults.Problem("This account is disabled.", statusCode: StatusCodes.Status400BadRequest);
            case not SignInStatus.Ok:
                return TypedResults.Problem("Wrong name or password.", statusCode: StatusCodes.Status400BadRequest);
        }

        await SfeSession.SignInAsync(context, result.User!, context.User.TenantId(), request.Remember);
        context.Response.Cookies.Delete(SfeSession.GuestTenantCookie);
        return TypedResults.Ok(await DescribeAsync(context));
    }

    private static async Task<Ok<SessionInfo>> SignOut(HttpContext context, CancellationToken cancellationToken)
    {
        await SfeSession.SignOutAsync(context);
        return TypedResults.Ok(await DescribeAsync(context));
    }

    private static async Task<Results<Ok<SessionInfo>, ProblemHttpResult>> ChooseGuestTenant(
        HttpContext context, AccessService accounts, CurrentAccess current, IOptionsMonitor<SecurityOptions> security, TenantChoice choice,
        CancellationToken cancellationToken)
    {
        var tenants = await accounts.GetCachedTenantsAsync(cancellationToken);
        var tenant = tenants.FirstOrDefault(t => string.Equals(t.Slug, choice.Tenant?.Trim(), StringComparison.OrdinalIgnoreCase));
        if (tenant is null || !tenant.GuestAccess || tenant.Disabled || !security.CurrentValue.AnonymousGuests)
            return TypedResults.Problem("This space is not open to guests.", statusCode: StatusCodes.Status404NotFound);

        // As a visitor without an account: a signed-in account browsing as a guest signs out first.
        await SfeSession.SignOutAsync(context);
        context.Response.Cookies.Append(SfeSession.GuestTenantCookie, tenant.Slug, new CookieOptions
        {
            HttpOnly = true,
            SameSite = SameSiteMode.Lax,
            IsEssential = true,
            Path = context.Request.PathBase.HasValue ? context.Request.PathBase.Value : "/",
            Expires = DateTimeOffset.UtcNow.AddYears(1),
        });
        current.Reset(tenant.Slug);
        return TypedResults.Ok(await DescribeAsync(context));
    }

    private static async Task<Results<Ok<SessionInfo>, UnauthorizedHttpResult, ProblemHttpResult>> SwitchTenant(
        HttpContext context, AccessService accounts, RightsEvaluator rights, CurrentAccess current, TenantChoice choice, CancellationToken cancellationToken)
    {
        if ((await current.GetAsync()).User is not { } user)
            return TypedResults.Unauthorized();

        var tenants = await accounts.GetCachedTenantsAsync(cancellationToken);
        var tenant = tenants.FirstOrDefault(t => string.Equals(t.Slug, choice.Tenant?.Trim(), StringComparison.OrdinalIgnoreCase));
        if (tenant is null || !(await rights.GetTenantIdsAsync(user.Id, cancellationToken)).Contains(tenant.Id))
            return TypedResults.Problem("This account has no access to that space.", statusCode: StatusCodes.Status404NotFound);

        await SfeSession.RenewAsync(context, user, tenant.Id);
        return TypedResults.Ok(await DescribeAsync(context));
    }

    private static async Task<Results<Ok<SessionInfo>, ValidationProblem, NotFound>> ChangePassword(
        HttpContext context, AccessService accounts, CurrentAccess current, PasswordChange change, CancellationToken cancellationToken)
    {
        var state = await current.GetAsync();
        var result = await accounts.ChangeOwnPasswordAsync(state.User!.Id, change.Current, change.Password, cancellationToken);
        if (result.Status == AccessStatus.NotFound)
            return TypedResults.NotFound();
        if (result.Status != AccessStatus.Ok)
            return TypedResults.ValidationProblem(result.Errors!);

        // The new stamp ended every session of the account, this one included: sign it in again.
        var user = (await accounts.GetSessionUserAsync(state.User.Id, cancellationToken))!;
        await SfeSession.RenewAsync(context, user, state.TenantId);
        return TypedResults.Ok(await DescribeAsync(context));
    }
}
