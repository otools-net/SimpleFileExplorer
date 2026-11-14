using Microsoft.Extensions.Options;
using Otools.Net.Sfe.Api.Security;
using Otools.Net.Sfe.Core.Library;
using Otools.Net.Sfe.Core.Options;
using Otools.Net.Sfe.Core.Paths;

namespace Otools.Net.Sfe.Api.Endpoints;

/// <summary>
/// What the routes of the library's folders and files take from the container and the request (bound with
/// <c>[AsParameters]</c>; none of it is in the OpenAPI document).
/// </summary>
internal sealed record LibraryRequest(
    LibraryBrowser Browser,
    LibraryPaths Paths,
    CurrentAccess Current,
    ServerFiles ServerFiles,
    IOptionsMonitor<SfeOptions> Options,
    ILoggerFactory LoggerFactory,
    HttpContext Context
);
