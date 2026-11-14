using Otools.Net.Sfe.Api.Security;
using Otools.Net.Sfe.Core.Files;
using Otools.Net.Sfe.Core.Paths;

namespace Otools.Net.Sfe.Api.Endpoints;

/// <summary>
/// What the routes of the trash take from the container and the request (bound with <c>[AsParameters]</c>; none of it
/// is in the OpenAPI document).
/// </summary>
internal sealed record TrashServices(
    Trash Trash,
    LibraryPaths Paths,
    ServerFiles ServerFiles,
    WorkFolder WorkFolder,
    CurrentAccess Current
);
