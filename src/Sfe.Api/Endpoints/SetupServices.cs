using Otools.Net.Sfe.Core.Access;
using Otools.Net.Sfe.Core.Settings;

namespace Otools.Net.Sfe.Api.Endpoints;

/// <summary>
/// What the route that completes the setup takes from the container (bound with <c>[AsParameters]</c>; none of it
/// is in the OpenAPI document). A record of its own: the route runs before anyone is signed in.
/// </summary>
internal sealed record SetupServices(
    SetupService Setup,
    AccessService Accounts,
    SettingLayers Settings,
    ILoggerFactory Loggers
);
