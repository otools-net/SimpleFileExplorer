using Microsoft.AspNetCore.Authorization;
using Microsoft.Extensions.Options;

namespace Sfe.Api.Security;

/// <summary>Builds the <c>right:a,b</c> policies on demand; other names go to the default provider.</summary>
public sealed class RightPolicyProvider(IOptions<AuthorizationOptions> options) : IAuthorizationPolicyProvider
{
    private readonly DefaultAuthorizationPolicyProvider _fallback = new(options);

    public Task<AuthorizationPolicy?> GetPolicyAsync(string policyName) =>
        RightAuthorization.RightsOf(policyName) is { } rights
            ? Task.FromResult<AuthorizationPolicy?>(
                new AuthorizationPolicyBuilder().AddRequirements(new RightRequirement(rights)).Build()
            )
            : _fallback.GetPolicyAsync(policyName);

    public Task<AuthorizationPolicy> GetDefaultPolicyAsync() => _fallback.GetDefaultPolicyAsync();

    public Task<AuthorizationPolicy?> GetFallbackPolicyAsync() => _fallback.GetFallbackPolicyAsync();
}
