using Sfe.Api.Tenancy;
using Sfe.Core.Access;

namespace Sfe.Api.Endpoints;

/// <summary>
/// What the routes of spaces take from the container (bound with <c>[AsParameters]</c>; none of it is in the OpenAPI
/// document).
/// </summary>
internal sealed record TenantServices(
    AccessService Accounts,
    TenantRegistry Registry,
    IConfiguration Configuration,
    ILoggerFactory Loggers
);
