using Otools.Net.Sfe.Api.Security;
using Otools.Net.Sfe.Core.Files;
using Otools.Net.Sfe.Core.Library;
using Otools.Net.Sfe.Core.Paths;

namespace Otools.Net.Sfe.Api.Endpoints;

/// <summary>
/// What the library's space used and free takes from the container (bound with <c>[AsParameters]</c>; none of it is
/// in the OpenAPI document).
/// </summary>
internal sealed record StorageServices(
    LibraryPaths Paths,
    CurrentAccess Current,
    LibraryBrowser Browser,
    Trash Trash,
    StorageUsageCache Cache
);
