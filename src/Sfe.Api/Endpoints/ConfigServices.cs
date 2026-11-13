using Microsoft.Extensions.Options;
using Sfe.Api.Security;
using Sfe.Core.Access;
using Sfe.Core.Connections;
using Sfe.Core.Options;
using Sfe.Core.Paths;
using Sfe.Core.Settings;
using Sfe.Core.Streaming;
using Sfe.Core.Thumbnails;
using Sfe.Core.Trickplay;

namespace Sfe.Api.Endpoints;

/// <summary>
/// What the client's configuration is made of (bound with <c>[AsParameters]</c>; none of it is in the OpenAPI
/// document).
/// </summary>
internal sealed record ConfigServices(
    LibraryPaths Paths,
    ThumbnailService Thumbnails,
    TrickplayService Trickplay,
    VideoStreamer Streamer,
    IOptionsMonitor<SfeOptions> Options,
    CurrentAccess Current,
    SettingsService Preferences,
    AccessService Accounts,
    RightsEvaluator Rights,
    IConfiguration Configuration,
    ConnectionProviders Connections
);
