using Sfe.Api.Security;
using Sfe.Core.Files;
using Sfe.Core.Paths;
using Sfe.Core.Settings;

namespace Sfe.Api.Endpoints;

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
