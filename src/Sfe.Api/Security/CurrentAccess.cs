using Sfe.Core.Access;

namespace Sfe.Api.Security;

/// <param name="Tenant">The library the request works in; null before the first start created one.</param>
/// <param name="User">The signed-in account; null for a visitor without one.</param>
public sealed record AccessState(TenantInfo? Tenant, SessionUser? User, EffectiveAccess Access)
{
    public int TenantId => Tenant?.Id ?? 0;
    public bool Has(string right) => Access.Has(right);
}

/// <summary>
/// Who makes the request and in which library, worked out once per request: a signed-in account works in the library
/// of its session (or the first one it reaches), a visitor without an account in the library it picked among those
/// open to guests (or the first library).
/// </summary>
public sealed class CurrentAccess(IHttpContextAccessor accessor, AccessService accounts, RightsEvaluator rights)
{
    private Task<AccessState>? _state;
    private string? _guestTenant;

    public Task<AccessState> GetAsync() => _state ??= ResolveAsync();

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
        var user = context.User.UserId() is { } userId ? await accounts.GetSessionUserAsync(userId, cancellationToken) : null;
        if (user is { Disabled: true })
            user = null;

        TenantInfo? tenant;
        if (user is not null)
        {
            var reachable = await rights.GetTenantIdsAsync(user.Id, cancellationToken);
            var wanted = context.User.TenantId() is { } chosen && reachable.Contains(chosen) ? chosen : reachable.FirstOrDefault();
            tenant = tenants.FirstOrDefault(t => t.Id == wanted) ?? tenants.FirstOrDefault(t => t.IsDefault);
        }
        else
        {
            var slug = _guestTenant ?? context.Request.Cookies[SfeSession.GuestTenantCookieName(context.Request)];
            tenant = tenants.FirstOrDefault(t => slug is not null && t.Slug == slug && t.GuestAccess && !t.Disabled)
                ?? tenants.FirstOrDefault(t => t.IsDefault);
        }

        var access = tenant is null ? EffectiveAccess.None : await rights.GetAsync(new AccessSubject(tenant.Id, user?.Id), cancellationToken);
        return new AccessState(tenant, user, access);
    }
}
