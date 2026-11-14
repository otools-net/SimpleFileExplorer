using Otools.Net.Sfe.Core.Access;

namespace Otools.Net.Sfe.Api.Security;

/// <param name="Tenant">The library the request works in; null before the first start created one.</param>
/// <param name="User">The signed-in account; null for a visitor without one.</param>
public sealed record AccessState(TenantInfo? Tenant, SessionUser? User, EffectiveAccess Access)
{
    public int TenantId => Tenant?.Id ?? 0;

    public bool Has(string right) => Access.Has(right);
}
