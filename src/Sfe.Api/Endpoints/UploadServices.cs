using Otools.Net.Sfe.Api.Security;
using Otools.Net.Sfe.Core.Files;
using Otools.Net.Sfe.Core.Paths;

namespace Otools.Net.Sfe.Api.Endpoints;

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
