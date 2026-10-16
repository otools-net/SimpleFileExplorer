using Sfe.Core.Paths;
using Microsoft.AspNetCore.Http.Features;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.Extensions.Options;
using Microsoft.Net.Http.Headers;
using Sfe.Api.Security;
using Sfe.Core.Access;
using Sfe.Core.Options;
using Sfe.Core.Streaming;

namespace Sfe.Api.Endpoints;

/// <param name="Duration">The video's length in seconds (the stream itself has none while it is made); null when unknown.</param>
/// <param name="Transcoded">Its pictures are encoded again (false: copied, which is lighter but seeks to key frames).</param>
public sealed record StreamProbe(double? Duration, bool Transcoded);

/// <param name="Available">ffmpeg was found and streaming is on in this library.</param>
/// <param name="Mode">When this library converts videos.</param>
/// <param name="MaxStreams">Videos the whole server converts at once.</param>
/// <param name="Streams">Running now, in every library, oldest first.</param>
public sealed record StreamingStatus(bool Available, StreamingMode Mode, int MaxStreams, IReadOnlyList<StreamActivity> Streams);

public static class StreamingEndpoints
{
    public static RouteGroupBuilder MapStreamingEndpoints(this RouteGroupBuilder api)
    {
        var streaming = api.MapGroup("/streaming").WithTags("Streaming");

        streaming.MapGet("/probe", Probe)
            .RequireRateLimiting(RequestLimits.Media)
            .RequireRight(Rights.Stream)
            .FolderScoped("files.stream at the video")
            .WithName("ProbeVideoStream")
            .WithSummary("Whether a video can be played converted by the server, and its length.")
            .WithDescription("404 when streaming is off (`Streaming:Mode`), ffmpeg is missing, or `path` is not a video of the library.");

        streaming.MapGet("/play", Play)
            .RequireRateLimiting(RequestLimits.Media)
            .RequireRight(Rights.Stream)
            .FolderScoped("files.stream and files.view at the video")
            .WithName("PlayVideoStream")
            .WithSummary("A video converted by ffmpeg while it plays (H.264 and AAC in fragmented MP4), from `t` seconds.")
            .WithDescription("""
                For browsers that can't play the file itself. The response has no length and no ranges: to seek, ask again
                with another `t` (its times start at 0 there). `id` names the player: a new request with the same `id`
                stops its earlier stream. 503 while the server converts `Streaming:MaxStreams` videos already.
                """);

        streaming.MapGet("/sessions", Sessions)
            .RequireRight(Rights.ViewStreams)
            .WithName("GetVideoStreams")
            .WithSummary("The videos the server converts now, in every library, with who watches them.");

        return api;
    }

    private static async Task<Results<Ok<StreamProbe>, NotFound>> Probe(
        VideoStreamer streamer, LibraryPaths paths, CurrentAccess current, IOptionsMonitor<SfeOptions> options, string path, CancellationToken cancellationToken)
    {
        if ((await current.GetAsync()).Check(paths, path, Rights.Stream) != PathVerdict.Allowed)
            return TypedResults.NotFound();
        var probe = await streamer.ProbeAsync(path, cancellationToken);
        return probe is null
            ? TypedResults.NotFound()
            : TypedResults.Ok(new StreamProbe(probe.Duration, !(options.CurrentValue.Streaming.CopyVideo && probe.BrowserPictures)));
    }

    private static async Task<Results<PushStreamHttpResult, NotFound, ForbidHttpResult, ProblemHttpResult>> Play(
        VideoStreamer streamer, LibraryPaths paths, CurrentAccess current, HttpContext context, string path, double t = 0, string? id = null)
    {
        var state = await current.GetAsync();
        if (!state.Has(Rights.ViewFiles))
            return TypedResults.Forbid();
        if (state.Check(paths, path, Rights.Stream) is var verdict and not PathVerdict.Allowed)
            return verdict == PathVerdict.NotFound ? TypedResults.NotFound() : TypedResults.Forbid();
        if (state.Check(paths, path, Rights.ViewFiles) != PathVerdict.Allowed)
            return TypedResults.Forbid();
        if (await streamer.ProbeAsync(path, context.RequestAborted) is not { } probe)
            return TypedResults.NotFound();

        var viewer = new StreamViewer(
            state.Tenant?.Name ?? string.Empty,
            state.User is { } user ? user.DisplayName ?? user.UserName : null,
            context.Connection.RemoteIpAddress?.ToString());
        // The player's id, kept apart per account: one viewer can't stop another's stream.
        var client = string.IsNullOrEmpty(id) ? null : $"{state.User?.Id}:{id}";
        if (streamer.TryBegin(path, probe, double.IsFinite(t) ? t : 0, viewer, client) is not { } stream)
        {
            return TypedResults.Problem(
                "The server is converting as many videos as it may at once: try again when one has ended.",
                statusCode: StatusCodes.Status503ServiceUnavailable);
        }

        context.Response.Headers[HeaderNames.CacheControl] = "no-store";
        context.Response.Headers[HeaderNames.AcceptRanges] = "none";
        context.Features.Get<IHttpResponseBodyFeature>()?.DisableBuffering();
        return TypedResults.Stream(body => streamer.RunAsync(stream, probe, body, context.RequestAborted), "video/mp4");
    }

    private static Ok<StreamingStatus> Sessions(VideoStreamer streamer, VideoStreams streams, IOptionsMonitor<SfeOptions> options) =>
        TypedResults.Ok(new StreamingStatus(streamer.IsAvailable, streamer.Mode, options.CurrentValue.Streaming.MaxStreams, streams.List()));
}
