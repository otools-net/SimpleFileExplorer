using Otools.Net.Sfe.Api.Security;
using Otools.Net.Sfe.Api.Tenancy;
using Otools.Net.Sfe.Core.Access;

namespace Otools.Net.Sfe.Api.Endpoints;

/// <summary>
/// What the routes of folder rules take from the container and the request (bound with <c>[AsParameters]</c>; none of
/// it is in the OpenAPI document).
/// </summary>
internal sealed record FolderRuleServices(
    FolderRuleService Rules,
    AccessService Accounts,
    TenantRegistry Tenants,
    CurrentAccess Current,
    ILoggerFactory Loggers
);
