using Otools.Net.Sfe.Api.Security;
using Otools.Net.Sfe.Api.Tenancy;
using Otools.Net.Sfe.Core.Access;

namespace Otools.Net.Sfe.Api.Endpoints;

/// <summary>
/// What a shared file link (no session: the token says whose file) takes from the container and the request
/// (bound with <c>[AsParameters]</c>; none of it is in the OpenAPI document).
/// </summary>
internal sealed record SharedFileServices(
    FileLinks Links,
    AccessService Accounts,
    RightsEvaluator Rights,
    TenantRegistry Tenants,
    ServerFiles ServerFiles,
    HttpContext Context
);
