using Microsoft.Extensions.Options;
using Sfe.Api.Security;
using Sfe.Core.Options;
using Sfe.Core.Paths;
using Sfe.Core.Streaming;

namespace Sfe.Api.Endpoints;

/// <summary>
/// What the routes of video streams take from the container (bound with <c>[AsParameters]</c>; none of it is in the
/// OpenAPI document).
/// </summary>
internal sealed record StreamingServices(
    VideoStreamer Streamer,
    LibraryPaths Paths,
    CurrentAccess Current,
    IOptionsMonitor<SfeOptions> Options
);
