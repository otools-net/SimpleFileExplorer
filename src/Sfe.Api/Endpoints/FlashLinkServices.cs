using Otools.Net.Sfe.Api.Security;
using Otools.Net.Sfe.Core.Files;
using Otools.Net.Sfe.Core.Paths;
using Otools.Net.Sfe.Core.Settings;

namespace Otools.Net.Sfe.Api.Endpoints;

/// <summary>
/// What the route that makes Flash links takes from the container (bound with <c>[AsParameters]</c>; none of it is
/// in the OpenAPI document).
/// </summary>
internal sealed record FlashLinkServices(
    LibraryPaths Paths,
    CurrentAccess Current,
    FlashLinks Links,
    ServerFiles ServerFiles,
    WorkFolder Work,
    SettingsService Settings
);
