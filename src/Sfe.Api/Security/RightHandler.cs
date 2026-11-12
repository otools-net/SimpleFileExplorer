using Microsoft.AspNetCore.Authorization;

namespace Sfe.Api.Security;

public sealed class RightHandler(CurrentAccess current) : AuthorizationHandler<RightRequirement>
{
    protected override async Task HandleRequirementAsync(
        AuthorizationHandlerContext context,
        RightRequirement requirement
    )
    {
        var access = await current.GetAsync();
        if (requirement.AnyOf.Any(access.Has))
            context.Succeed(requirement);
    }
}
