using Sfe.Api.Security;
using Sfe.Core.Paths;
using Sfe.Core.Tagging;

namespace Sfe.Api.Endpoints;

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
