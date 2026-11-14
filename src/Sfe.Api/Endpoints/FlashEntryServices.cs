using Otools.Net.Sfe.Api.Security;
using Otools.Net.Sfe.Api.Tenancy;
using Otools.Net.Sfe.Core.Access;
using Otools.Net.Sfe.Core.Settings;

namespace Otools.Net.Sfe.Api.Endpoints;

/// <summary>
/// What the route of a Flash movie's files takes from the container (bound with <c>[AsParameters]</c>; none of it
/// is in the OpenAPI document). A record of its own: the route runs without a session, on the token alone.
/// </summary>
internal sealed record FlashEntryServices(
    FlashLinks Links,
    AccessService Accounts,
    RightsEvaluator Evaluator,
    TenantRegistry Tenants,
    ServerFiles ServerFiles,
    SettingsService Settings
);
