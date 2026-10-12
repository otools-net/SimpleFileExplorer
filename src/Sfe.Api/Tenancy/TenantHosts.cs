using Sfe.Core.Access;

namespace Sfe.Api.Tenancy;

/// <summary>
/// The services (index, paths, thumbnails...) of a library, for the requests that name their library themselves
/// rather than through the session (an external player's link).
/// </summary>
public sealed class TenantHosts(IServiceProvider root, AccessService accounts)
{
    /// <summary>The library's services; null when it doesn't exist or is disabled.</summary>
    public async Task<IServiceProvider?> GetServicesAsync(int tenantId, CancellationToken cancellationToken = default)
    {
        var tenant = (await accounts.GetCachedTenantsAsync(cancellationToken)).FirstOrDefault(t => t.Id == tenantId);
        return tenant is { IsDefault: true, Disabled: false } ? root : null;
    }
}
