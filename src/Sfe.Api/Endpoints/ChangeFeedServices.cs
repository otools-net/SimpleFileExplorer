using Otools.Net.Sfe.Api.Security;
using Otools.Net.Sfe.Core.Files;
using Otools.Net.Sfe.Core.KeepAlive;
using Otools.Net.Sfe.Core.Library;
using Otools.Net.Sfe.Core.Paths;

namespace Otools.Net.Sfe.Api.Endpoints;

/// <summary>
/// What the change feed takes from the container for the library's folders, keep alive and the trash (bound with
/// <c>[AsParameters]</c>; none of it is in the OpenAPI document).
/// </summary>
internal sealed record ChangeFeedServices(
    LibraryBrowser Browser,
    KeepAliveService KeepAlive,
    Trash Trash,
    LibraryPaths Paths,
    CurrentAccess Current,
    IHttpContextAccessor Accessor
);
