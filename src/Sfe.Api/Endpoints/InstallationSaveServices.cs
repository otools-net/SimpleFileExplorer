using Otools.Net.Sfe.Api.Maintenance;
using Otools.Net.Sfe.Api.Security;
using Otools.Net.Sfe.Api.Tenancy;
using Otools.Net.Sfe.Core.Access;

namespace Otools.Net.Sfe.Api.Endpoints;

/// <summary>
/// What the route that saves the installation settings takes from the container (bound with
/// <c>[AsParameters]</c>; none of it is in the OpenAPI document).
/// </summary>
internal sealed record InstallationSaveServices(
    InstallationAccess Access,
    InstallationSettings Settings,
    TenantRegistry Libraries,
    AccessService Accounts,
    SetupService Setup,
    IConfiguration Configuration,
    ILoggerFactory Loggers
);
