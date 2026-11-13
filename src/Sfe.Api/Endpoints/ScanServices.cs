using Microsoft.Extensions.Options;
using Sfe.Api.Security;
using Sfe.Core.Indexing;
using Sfe.Core.Options;
using Sfe.Core.Paths;
using Sfe.Core.Settings;

namespace Sfe.Api.Endpoints;

/// <summary>
/// What the routes of the index take from the container (bound with <c>[AsParameters]</c>; none of it is in the
/// OpenAPI document).
/// </summary>
internal sealed record ScanServices(
    ScanCoordinator Coordinator,
    DirectoryIndexer Indexer,
    SettingsService Settings,
    LibraryPaths Paths,
    CurrentAccess Current,
    IOptionsMonitor<SfeOptions> Options,
    ILoggerFactory Loggers
);
