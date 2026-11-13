using Sfe.Api.Maintenance;
using Sfe.Api.Security;
using Sfe.Api.Tenancy;
using Sfe.Core.Access;

namespace Sfe.Api.Endpoints;

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
