using Otools.Net.Sfe.Api.Maintenance;
using Otools.Net.Sfe.Api.Security;

namespace Otools.Net.Sfe.Api.Endpoints;

/// <summary>
/// What the route that checks the setup code takes from the container (bound with <c>[AsParameters]</c>; none of
/// it is in the OpenAPI document).
/// </summary>
internal sealed record InstallationServices(
    InstallationAccess Access,
    InstallationSettings Settings,
    ILoggerFactory Loggers
);
