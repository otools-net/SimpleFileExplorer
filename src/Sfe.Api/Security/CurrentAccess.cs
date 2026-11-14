using System.Globalization;
using Sfe.Api.Tenancy;
using Sfe.Core.Access;

namespace Sfe.Api.Security;

/// <summary>
/// Who makes the request and in which library, worked out once per request: a signed-in account works in the library
/// of its session (or the first one it reaches), a visitor without an account in the library it picked among those
/// open to guests (or the first library). A request made with an access token (<see cref="BearerTokens"/>) works in
/// the token's library, or the one it names (<see cref="TenantMiddleware.Header"/>) among those the account reaches,
/// else the account's first; its rights are the account's, narrowed to the token's when it lists some.
/// </summary>
public sealed class CurrentAccess(
    IHttpContextAccessor accessor,
    AccessService accounts,
    RightsEvaluator rights,
    AccessTokens tokens
)
{
    private static readonly AccessState Nobody = new(null, null, EffectiveAccess.None);

    private Task<AccessState>? _state;
    private string? _guestTenant;

    public Task<AccessState> GetAsync() => _state ??= ResolveAsync();

    /// <summary>
    /// The access token of the request, for a job it starts to keep checking (<see cref="JobToken"/>); null for a
    /// session or a visitor.
    /// </summary>
    public JobToken? JobToken() =>
        accessor.HttpContext is { } context && BearerTokens.TokenOf(context) is { } token ? new(token, tokens) : null;

    /// <summary>Forgets the state read so far, after the request signed in or out.</summary>
    /// <param name="guestTenant">The library a visitor just picked, before the browser sends its cookie back.</param>
    public void Reset(string? guestTenant = null)
    {
        _state = null;
        _guestTenant = guestTenant ?? _guestTenant;
    }

    private async Task<AccessState> ResolveAsync()
    {
        var context = accessor.HttpContext ?? throw new InvalidOperationException("No request.");
        var cancellationToken = context.RequestAborted;
        var tenants = await accounts.GetCachedTenantsAsync(cancellationToken);
        var user = context.User.UserId() is { } userId
            ? await accounts.GetSessionUserAsync(userId, cancellationToken)
            : null;
        if (user is { Disabled: true })
            user = null;

        var token = BearerTokens.TokenOf(context);
        if (token is not null && user is null)
            return Nobody;

        TenantInfo? tenant;
        if (user is null)
            tenant = GuestTenant(context, tenants);
        else if (token is null)
            tenant = await SessionTenantAsync(context, user, tenants, cancellationToken);
        else
            tenant = await TokenTenantAsync(context, user, token, tenants, cancellationToken);

        var access = tenant is null
            ? EffectiveAccess.None
            : await rights.GetAsync(new AccessSubject(tenant.Id, user?.Id), cancellationToken);
        return new AccessState(tenant, user, BearerTokens.Limited(access, token));
    }

    /// <summary>The session's library if the account still reaches it, else its first.</summary>
    private async Task<TenantInfo?> SessionTenantAsync(
        HttpContext context,
        SessionUser user,
        IReadOnlyList<TenantInfo> tenants,
        CancellationToken cancellationToken
    )
    {
        var reachable = await rights.GetTenantIdsAsync(user.Id, cancellationToken);
        var wanted =
            context.User.TenantId() is { } chosen && reachable.Contains(chosen) ? chosen : reachable.FirstOrDefault();
        return tenants.FirstOrDefault(t => t.Id == wanted) ?? tenants.FirstOrDefault(t => t.IsDefault);
    }

    /// <summary>
    /// The token's library, or the one the request names, when the account reaches it; none (no rights at all) for a
    /// library the token isn't for or the account doesn't reach.
    /// </summary>
    private async Task<TenantInfo?> TokenTenantAsync(
        HttpContext context,
        SessionUser user,
        PresentedAccessToken token,
        IReadOnlyList<TenantInfo> tenants,
        CancellationToken cancellationToken
    )
    {
        var named = NamedTenant(context.Request);
        if (token.TenantId is { } only && named is { } other && other != only)
            return null;

        var reachable = await rights.GetTenantIdsAsync(user.Id, cancellationToken);
        var wanted = token.TenantId ?? named;
        if (wanted is null)
            return await SessionTenantAsync(context, user, tenants, cancellationToken);
        return reachable.Contains(wanted.Value) ? tenants.FirstOrDefault(t => t.Id == wanted) : null;
    }

    private TenantInfo? GuestTenant(HttpContext context, IReadOnlyList<TenantInfo> tenants)
    {
        var slug = _guestTenant ?? context.Request.Cookies[SfeSession.GuestTenantCookieName(context.Request)];
        return tenants.FirstOrDefault(t => slug is not null && t.Slug == slug && t.GuestAccess && !t.Disabled)
            ?? tenants.FirstOrDefault(t => t.IsDefault);
    }

    /// <summary>The library a request names (<see cref="TenantMiddleware.Header"/>); null without one.</summary>
    private static int? NamedTenant(HttpRequest request) =>
        int.TryParse(
            request.Headers[TenantMiddleware.Header].ToString(),
            NumberStyles.None,
            CultureInfo.InvariantCulture,
            out var id
        )
            ? id
            : null;
}
