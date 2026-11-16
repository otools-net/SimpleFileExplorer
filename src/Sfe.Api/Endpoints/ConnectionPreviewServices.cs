using Microsoft.Extensions.Options;
using Otools.Net.Sfe.Core.Connections.Indexing;
using Otools.Net.Sfe.Core.Connections.Previews;
using Otools.Net.Sfe.Core.Library;
using Otools.Net.Sfe.Core.Options;
using Otools.Net.Sfe.Core.Streaming;
using Otools.Net.Sfe.Core.Thumbnails;

namespace Otools.Net.Sfe.Api.Endpoints;

/// <summary>
/// What the preview routes of a connection's files use besides the connection (bound with <c>[AsParameters]</c>):
/// the library's previews and rows, the thumbnails' and streams' switches.
/// </summary>
internal sealed record ConnectionPreviewServices(
    ConnectionPreviews Previews,
    RemoteMediaRows Rows,
    MediaClassifier Classifier,
    ThumbnailService Thumbnails,
    VideoStreamer Streamer,
    IOptionsMonitor<SfeOptions> Options
);
