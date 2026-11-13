using Sfe.Api.Security;
using Sfe.Api.Tenancy;
using Sfe.Core.Access;

namespace Sfe.Api.Endpoints;

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
