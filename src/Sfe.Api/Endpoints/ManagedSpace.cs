using Otools.Net.Sfe.Api.Security;
using Otools.Net.Sfe.Api.Tenancy;
using Otools.Net.Sfe.Core.Access;
using Otools.Net.Sfe.Core.Paths;

namespace Otools.Net.Sfe.Api.Endpoints;

/// <summary>
/// The space whose folder rules a request manages (<see cref="FolderRuleEndpoints"/>), with its library: the
/// request's own, or another one for server administrators.
/// </summary>
internal sealed record ManagedSpace(int Id, AccessState State, TenantHost Host, LibraryPaths Paths)
{
    /// <summary>
    /// The space a request is about, with its library; null when the caller may not manage it (as in
    /// <see cref="AccessEndpoints"/>).
    /// </summary>
    public static async Task<ManagedSpace?> FindAsync(
        CurrentAccess current,
        TenantRegistry tenants,
        int? tenant,
        CancellationToken cancellationToken
    )
    {
        var state = await current.GetAsync();
        var id =
            tenant is not { } asked || asked == state.TenantId ? state.TenantId
            : state.Has(Rights.ManageTenants) ? asked
            : (int?)null;
        if (id is not { } spaceId || await tenants.GetAsync(spaceId, cancellationToken) is not { } host)
            return null;
        return new ManagedSpace(spaceId, state, host, host.Services.GetRequiredService<LibraryPaths>());
    }
}
