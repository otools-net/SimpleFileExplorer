using Microsoft.Extensions.Options;
using Otools.Net.Sfe.Api.Security;
using Otools.Net.Sfe.Core.Indexing;
using Otools.Net.Sfe.Core.Options;
using Otools.Net.Sfe.Core.Paths;
using Otools.Net.Sfe.Core.Settings;

namespace Otools.Net.Sfe.Api.Endpoints;

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
