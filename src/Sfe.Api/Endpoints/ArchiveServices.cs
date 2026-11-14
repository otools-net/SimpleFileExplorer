using Otools.Net.Sfe.Api.Security;
using Otools.Net.Sfe.Core.Files;
using Otools.Net.Sfe.Core.Paths;

namespace Otools.Net.Sfe.Api.Endpoints;

/// <summary>
/// What the routes of zip downloads take from the container (bound with <c>[AsParameters]</c>; none of it is in the
/// OpenAPI document).
/// </summary>
internal sealed record ArchiveServices(
    Archives Archives,
    ArchiveLinks Links,
    LibraryPaths Paths,
    ServerFiles ServerFiles,
    WorkFolder Work,
    CurrentAccess Current,
    ILogger<Archives> Logger
);
