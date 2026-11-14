using Microsoft.Extensions.Options;
using Otools.Net.Sfe.Api.Security;
using Otools.Net.Sfe.Core.Files;
using Otools.Net.Sfe.Core.Files.Compression;
using Otools.Net.Sfe.Core.Library;
using Otools.Net.Sfe.Core.Options;
using Otools.Net.Sfe.Core.Paths;

namespace Otools.Net.Sfe.Api.Endpoints;

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
