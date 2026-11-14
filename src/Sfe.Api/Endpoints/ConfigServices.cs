using Microsoft.Extensions.Options;
using Otools.Net.Sfe.Api.Security;
using Otools.Net.Sfe.Core.Access;
using Otools.Net.Sfe.Core.Connections;
using Otools.Net.Sfe.Core.Options;
using Otools.Net.Sfe.Core.Paths;
using Otools.Net.Sfe.Core.Settings;
using Otools.Net.Sfe.Core.Streaming;
using Otools.Net.Sfe.Core.Thumbnails;
using Otools.Net.Sfe.Core.Trickplay;

namespace Otools.Net.Sfe.Api.Endpoints;

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
    ConnectionProviders Connections,
    AccessTokens Tokens
);
