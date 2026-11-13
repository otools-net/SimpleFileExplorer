using Sfe.Api.Security;
using Sfe.Core.Files;
using Sfe.Core.Paths;

namespace Sfe.Api.Endpoints;

/// <summary>
/// What the routes of duplicates, similar videos and missing files take from the container and the request (bound with
/// <c>[AsParameters]</c>; none of it is in the OpenAPI document).
/// </summary>
internal sealed record FileTrackingServices(
    LibraryPaths Paths,
    ServerFiles ServerFiles,
    WorkFolder Work,
    CurrentAccess Current
);
