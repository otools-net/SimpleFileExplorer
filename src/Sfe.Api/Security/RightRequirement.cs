using Microsoft.AspNetCore.Authorization;

namespace Sfe.Api.Security;

public sealed record RightRequirement(IReadOnlyList<string> AnyOf) : IAuthorizationRequirement;
