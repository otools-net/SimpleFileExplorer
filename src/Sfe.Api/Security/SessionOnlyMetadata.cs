namespace Otools.Net.Sfe.Api.Security;

/// <summary>
/// A route that a personal access token may not call (<see cref="SessionOnlyRoutes"/>); read by the request guard,
/// the OpenAPI document (<c>x-sfe-session-only</c>) and the test checking which routes carry it.
/// </summary>
public sealed record SessionOnlyMetadata;
