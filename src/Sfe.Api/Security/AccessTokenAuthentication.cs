using System.Globalization;
using System.Security.Claims;
using System.Text.Encodings.Web;
using Microsoft.AspNetCore.Authentication;
using Microsoft.Extensions.Options;
using Otools.Net.Sfe.Api.Maintenance;
using Otools.Net.Sfe.Core.Access;
using Otools.Net.Sfe.Core.Logs;

namespace Otools.Net.Sfe.Api.Security;

/// <summary>
/// The personal access tokens' scheme (agents/docs/development/api-tokens-plan.md): a request sending
/// <c>Authorization: Bearer sfe_…</c> is the token's account, with the same claims as its session cookie plus the
/// token's id (<see cref="TokenClaim"/>); its limits go to <see cref="CurrentAccess"/> as a request feature
/// (<see cref="BearerTokens.TokenOf"/>). The cookie's own check (<see cref="SfeSession.ValidateAsync"/>) doesn't run
/// here: the account is read again at each request, a disabled or deleted one refused, and a Local server takes
/// administrators only. Wrong tokens count against their address (<see cref="SignInThrottle"/>, as sign-ins), and
/// the log names them by their lookup part only, never by their secret.
/// </summary>
public sealed class AccessTokenAuthentication(
    IOptionsMonitor<AuthenticationSchemeOptions> options,
    ILoggerFactory loggers,
    UrlEncoder encoder,
    AccessTokens tokens,
    AccessService accounts,
    SignInThrottle throttle,
    MigrationCoordinator migration,
    IConfiguration configuration
) : AuthenticationHandler<AuthenticationSchemeOptions>(options, loggers, encoder)
{
    public const string SchemeName = "AccessToken";

    /// <summary>The scheme every request starts with: the token's with a bearer, otherwise the cookie's.</summary>
    public const string SelectingScheme = "Sfe";

    /// <summary>The id of the token the request came with.</summary>
    public const string TokenClaim = "sfe:token";

    /// <summary>
    /// The name wrong tokens are counted under in <see cref="SignInThrottle"/>, beside the address: no account name
    /// holds a control character, so it never shares a count with a sign-in.
    /// </summary>
    internal const string ThrottleName = "\u0001access-token";

    protected override async Task<AuthenticateResult> HandleAuthenticateAsync()
    {
        if (BearerTokens.Presented(Request) is not { } presented)
            return AuthenticateResult.NoResult();
        // An install waiting for its update may lack the tokens' table: nothing but the update page answers anyway.
        if (migration.IsBlocking)
            return AuthenticateResult.Fail("SFE waits for its update.");
        if (!tokens.Options.Enabled)
            return AuthenticateResult.Fail("Access tokens are turned off.");

        var caller = AddressKeys.Of(Context);
        if (throttle.LockedUntil(ThrottleName, caller) is not null)
            return AuthenticateResult.Fail("Too many wrong tokens from this address.");

        var token = await tokens.FindAsync(presented, Address, Context.RequestAborted);
        if (token is null)
            return Refused(presented, caller);
        return await AcceptAsync(token);
    }

    /// <summary>A 401 that says it wants a token, for programs (no cookie is touched, nothing redirects).</summary>
    protected override Task HandleChallengeAsync(AuthenticationProperties properties)
    {
        Response.StatusCode = StatusCodes.Status401Unauthorized;
        Response.Headers.WWWAuthenticate = "Bearer error=\"invalid_token\"";
        return Task.CompletedTask;
    }

    private string? Address => Context.Connection.RemoteIpAddress?.ToString();

    /// <summary>The token's account, when it may still use the server.</summary>
    private async Task<AuthenticateResult> AcceptAsync(PresentedAccessToken token)
    {
        var user = await accounts.GetSessionUserAsync(token.UserId, Context.RequestAborted);
        if (user is null || user.Disabled)
        {
            Logger.LogSecurityWarning(
                "Access token {TokenId} “{TokenName}” refused from {Address}: its account is disabled or deleted",
                token.Id,
                token.Name,
                Address
            );
            return AuthenticateResult.Fail("The token's account is disabled.");
        }

        // A Local server lets only administrators in (ServerModes), whatever they sign in with.
        var isLocal = ServerModes.IsLocal(configuration);
        if (isLocal && !await accounts.IsAdministratorAsync(user.Id, Context.RequestAborted))
        {
            Logger.LogSecurityWarning(
                "Access token {TokenId} “{TokenName}” of {User} refused from {Address}: SFE is in Local mode, only "
                    + "administrators use it",
                token.Id,
                token.Name,
                user.UserName,
                Address
            );
            return AuthenticateResult.Fail("SFE is in Local mode.");
        }

        Context.Features.Set(token);
        return AuthenticateResult.Success(new AuthenticationTicket(Principal(user, token), SchemeName));
    }

    private AuthenticateResult Refused(string presented, string caller)
    {
        var lockedUntil = throttle.Failed(ThrottleName, caller);
        // The lookup part names the row (lists show it at the start of the prefix); the secret never goes anywhere.
        var named = AccessTokenSecret.TryParse(presented, out var lookup, out _)
            ? AccessTokenSecret.Scheme + lookup
            : "not an SFE token";
        if (lockedUntil is { } until)
        {
            Logger.LogSecurityWarning(
                "Access token {Token} refused from {Address}: unknown, wrong, revoked or expired; that address is now "
                    + "refused every token until {Until:u}",
                named,
                Address,
                until
            );
        }
        else
        {
            Logger.LogSecurityWarning(
                "Access token {Token} refused from {Address}: unknown, wrong, revoked or expired",
                named,
                Address
            );
        }
        return AuthenticateResult.Fail("Unknown, wrong, revoked or expired token.");
    }

    private static ClaimsPrincipal Principal(SessionUser user, PresentedAccessToken token)
    {
        List<Claim> claims =
        [
            new(ClaimTypes.NameIdentifier, user.Id.ToString(CultureInfo.InvariantCulture)),
            new(ClaimTypes.Name, user.UserName),
            new(SfeSession.StampClaim, user.SecurityStamp),
            new(TokenClaim, token.Id.ToString(CultureInfo.InvariantCulture)),
        ];
        if (token.TenantId is { } tenant)
            claims.Add(new Claim(SfeSession.TenantClaim, tenant.ToString(CultureInfo.InvariantCulture)));
        return new ClaimsPrincipal(new ClaimsIdentity(claims, SchemeName));
    }
}
