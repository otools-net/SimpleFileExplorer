using Microsoft.Extensions.Options;
using Sfe.Api.Security;
using Sfe.Core.Files;
using Sfe.Core.Files.Compression;
using Sfe.Core.Library;
using Sfe.Core.Options;
using Sfe.Core.Paths;

namespace Sfe.Api.Endpoints;

/// <summary>
/// What every route of the archive explorer takes from the container and the request (bound with
/// <c>[AsParameters]</c>; none of it is in the OpenAPI document).
/// </summary>
internal sealed record ExploreServices(
    ArchiveCatalogs Catalogs,
    IOptionsMonitor<SfeOptions> Options,
    LibraryPaths Paths,
    ServerFiles ServerFiles,
    WorkFolder Work,
    ArchiveUnlocks Unlocks,
    CatalogBuildLimits Limits,
    MediaClassifier Media,
    CurrentAccess Current,
    HttpContext Context
);
