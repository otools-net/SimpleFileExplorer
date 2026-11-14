using Otools.Net.Sfe.Api.Security;
using Otools.Net.Sfe.Core.Paths;
using Otools.Net.Sfe.Core.Tagging;

namespace Otools.Net.Sfe.Api.Endpoints;

/// <summary>
/// What the routes of tags take from the container (bound with <c>[AsParameters]</c>; none of it is in the OpenAPI
/// document).
/// </summary>
internal sealed record TagServices(
    FolderDefaultTags Defaults,
    ScopeTagger Tagger,
    LibraryPaths Paths,
    CurrentAccess Current,
    ILoggerFactory Loggers
);
