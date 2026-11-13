using Sfe.Api.Maintenance;
using Sfe.Api.Security;

namespace Sfe.Api.Endpoints;

/// <summary>
/// What the route that checks the setup code takes from the container (bound with <c>[AsParameters]</c>; none of
/// it is in the OpenAPI document).
/// </summary>
internal sealed record InstallationServices(
    InstallationAccess Access,
    InstallationSettings Settings,
    ILoggerFactory Loggers
);
