using Otools.Net.Sfe.Api.Security;

namespace Otools.Net.Sfe.Api.Endpoints;

/// <summary>
/// What a refused request tried, for <see cref="FileEndpoints.Refused"/>: who, at which path (as the request
/// named it), doing what (words for the security warning: "rename", "create a file in").
/// </summary>
internal sealed record RefusedAttempt(AccessState State, string? Path, string Action);
