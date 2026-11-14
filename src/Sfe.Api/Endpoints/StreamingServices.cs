using Microsoft.Extensions.Options;
using Otools.Net.Sfe.Api.Security;
using Otools.Net.Sfe.Core.Options;
using Otools.Net.Sfe.Core.Paths;
using Otools.Net.Sfe.Core.Streaming;

namespace Otools.Net.Sfe.Api.Endpoints;

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
