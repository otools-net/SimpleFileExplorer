using Sfe.Api.Security;
using Sfe.Core.Files;
using Sfe.Core.Paths;

namespace Sfe.Api.Endpoints;

/// <summary>
/// What the routes creating, renaming and deleting files take from the container and the request (bound with
/// <c>[AsParameters]</c>; none of it is in the OpenAPI document).
/// </summary>
internal sealed record FileServices(
    FileOperations Operations,
    LibraryPaths Paths,
    ServerFiles ServerFiles,
    WorkFolder WorkFolder,
    CurrentAccess Current,
    ILoggerFactory Loggers
);
