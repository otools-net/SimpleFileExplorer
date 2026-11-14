using Microsoft.AspNetCore.Authorization;

namespace Otools.Net.Sfe.Api.Security;

public sealed record RightRequirement(IReadOnlyList<string> AnyOf) : IAuthorizationRequirement;
