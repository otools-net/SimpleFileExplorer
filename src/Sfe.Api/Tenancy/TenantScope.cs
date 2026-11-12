using Sfe.Api.Security;

namespace Sfe.Api.Tenancy;

/// <summary>
/// The services of the request's library, found once its session is known (<see cref="TenantMiddleware"/>).
/// </summary>
public sealed class TenantScope(TenantRegistry registry, CurrentAccess current)
{
    private TenantHost? _host;

    public TenantHost Host =>
        _host
        ?? throw new InvalidOperationException(
            "The request's library is not known (it is unavailable, or the request is not an API one)."
        );

    public async Task<TenantHost?> InitializeAsync(CancellationToken cancellationToken)
    {
        var state = await current.GetAsync();
        _host = state.Tenant is null ? null : await registry.GetAsync(state.TenantId, cancellationToken);
        return _host;
    }
}
