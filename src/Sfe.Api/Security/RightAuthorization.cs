using Microsoft.AspNetCore.Authorization;
using Microsoft.Extensions.Options;

namespace Sfe.Api.Security;

/// <summary>The rights a route needs (any one of them), read by the test checking that every route names some.</summary>
public sealed record RightMetadata(IReadOnlyList<string> AnyOf);

public static class RightAuthorization
{
    private const string PolicyPrefix = "right:";

    /// <summary>
    /// The route needs one of <paramref name="anyOf"/> in the request's library (<see cref="CurrentAccess"/>).
    /// Visitors without an account pass too when the library's guests have it; otherwise they get 401, and accounts
    /// without the right 403.
    /// </summary>
    public static TBuilder RequireRight<TBuilder>(this TBuilder builder, params string[] anyOf) where TBuilder : IEndpointConventionBuilder =>
        builder
            .WithMetadata(new RightMetadata(anyOf))
            .RequireAuthorization(PolicyPrefix + string.Join(',', anyOf));

    internal static IReadOnlyList<string>? RightsOf(string policyName) =>
        policyName.StartsWith(PolicyPrefix, StringComparison.Ordinal) ? policyName[PolicyPrefix.Length..].Split(',') : null;
}

public sealed record RightRequirement(IReadOnlyList<string> AnyOf) : IAuthorizationRequirement;

/// <summary>Builds the <c>right:a,b</c> policies on demand; other names go to the default provider.</summary>
public sealed class RightPolicyProvider(IOptions<AuthorizationOptions> options) : IAuthorizationPolicyProvider
{
    private readonly DefaultAuthorizationPolicyProvider _fallback = new(options);

    public Task<AuthorizationPolicy?> GetPolicyAsync(string policyName) =>
        RightAuthorization.RightsOf(policyName) is { } rights
            ? Task.FromResult<AuthorizationPolicy?>(new AuthorizationPolicyBuilder().AddRequirements(new RightRequirement(rights)).Build())
            : _fallback.GetPolicyAsync(policyName);

    public Task<AuthorizationPolicy> GetDefaultPolicyAsync() => _fallback.GetDefaultPolicyAsync();

    public Task<AuthorizationPolicy?> GetFallbackPolicyAsync() => _fallback.GetFallbackPolicyAsync();
}

public sealed class RightHandler(CurrentAccess current) : AuthorizationHandler<RightRequirement>
{
    protected override async Task HandleRequirementAsync(AuthorizationHandlerContext context, RightRequirement requirement)
    {
        var access = await current.GetAsync();
        if (requirement.AnyOf.Any(access.Has))
            context.Succeed(requirement);
    }
}
