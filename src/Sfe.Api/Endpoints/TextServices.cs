using Sfe.Api.Security;
using Sfe.Core.Files;
using Sfe.Core.Paths;

namespace Sfe.Api.Endpoints;

/// <summary>
/// What the routes of text files take from the container and the request (bound with <c>[AsParameters]</c>; none of it
/// is in the OpenAPI document).
/// </summary>
internal sealed record TextServices(
    FileContent Content,
    Trash Trash,
    LibraryPaths Paths,
    ServerFiles ServerFiles,
    WorkFolder Work,
    CurrentAccess Current,
    HttpContext Context,
    ILoggerFactory Loggers
);
