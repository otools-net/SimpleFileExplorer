using Sfe.Api.Security;
using Sfe.Core.Files;
using Sfe.Core.Paths;

namespace Sfe.Api.Endpoints;

/// <summary>
/// What the routes of uploads take from the container and the request (bound with <c>[AsParameters]</c>; none of it is
/// in the OpenAPI document).
/// </summary>
internal sealed record UploadServices(
    Uploads Uploads,
    LibraryPaths Paths,
    ServerFiles ServerFiles,
    WorkFolder Work,
    CurrentAccess Current
);
