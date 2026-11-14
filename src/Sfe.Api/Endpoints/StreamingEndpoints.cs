using Microsoft.AspNetCore.Http.Features;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.Extensions.Options;
using Microsoft.Net.Http.Headers;
using Otools.Net.Sfe.Api.Security;
using Otools.Net.Sfe.Core.Access;
using Otools.Net.Sfe.Core.Options;
using Otools.Net.Sfe.Core.Streaming;

namespace Otools.Net.Sfe.Api.Endpoints;

public static class StreamingEndpoints
{
    private const string ProbeVideoStreamDescription =
        "404 when streaming is off (`Streaming:Mode`), ffmpeg is missing, or `path` is not a video of the "
        + "library.";

    private const string PlayVideoStreamDescription = """
        For browsers that can't play the file itself. The response has no length and no ranges: to seek, ask
        again with another `t` (its times start at 0 there). `id` names the player: a new request with the same
        `id` stops its earlier stream. 503 while the server converts `Streaming:MaxStreams` videos already.
        """;

    public static RouteGroupBuilder MapStreamingEndpoints(this RouteGroupBuilder api)
    {
        var streaming = api.MapGroup("/streaming").WithTags("Streaming");

        streaming
            .MapGet("/probe", Probe)
            .RequireRateLimiting(RequestLimits.Media)
            .RequireRight(Rights.Stream)
            .FolderScoped("files.stream at the video")
            .WithName("ProbeVideoStream")
            .WithSummary("Whether a video can be played converted by the server, and its length.")
            .WithDescription(ProbeVideoStreamDescription);

        streaming
            .MapGet("/play", Play)
            .RequireRateLimiting(RequestLimits.Media)
            .RequireRight(Rights.Stream)
            .FolderScoped("files.stream and files.view at the video")
            .WithName("PlayVideoStream")
            .WithSummary(
                "A video converted by ffmpeg while it plays (H.264 and AAC in fragmented MP4), from `t` seconds."
            )
            .WithDescription(PlayVideoStreamDescription);

        streaming
            .MapGet("/sessions", Sessions)
            .RequireRight(Rights.ViewStreams)
            .WithName("GetVideoStreams")
            .WithSummary("The videos the server converts now, in every library, with who watches them.");

        return api;
    }

    private static async Task<Results<Ok<StreamProbe>, NotFound>> Probe(
        [AsParameters] StreamingServices services,
        string path,
        CancellationToken cancellationToken
    )
    {
        var (streamer, paths, current, options) = services;
        if ((await current.GetAsync()).Check(paths, path, Rights.Stream) != PathVerdict.Allowed)
            return TypedResults.NotFound();
        var probe = await streamer.ProbeAsync(path, cancellationToken);
        return probe is null
            ? TypedResults.NotFound()
            : TypedResults.Ok(
                new StreamProbe(probe.Duration, !(options.CurrentValue.Streaming.CopyVideo && probe.BrowserPictures))
            );
    }

    private static async Task<Results<PushStreamHttpResult, NotFound, ForbidHttpResult, ProblemHttpResult>> Play(
        [AsParameters] StreamingServices services,
        HttpContext context,
        string path,
        double t = 0,
        string? id = null
    )
    {
        var (streamer, paths, current, _) = services;
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
            context.Connection.RemoteIpAddress?.ToString()
        );
        // The player's id, kept apart per account: one viewer can't stop another's stream.
        var client = string.IsNullOrEmpty(id) ? null : $"{state.User?.Id}:{id}";
        if (streamer.TryBegin(path, probe, double.IsFinite(t) ? t : 0, viewer, client) is not { } stream)
        {
            return TypedResults.Problem(
                "The server is converting as many videos as it may at once: try again when one has ended.",
                statusCode: StatusCodes.Status503ServiceUnavailable
            );
        }

        context.Response.Headers[HeaderNames.CacheControl] = "no-store";
        context.Response.Headers[HeaderNames.AcceptRanges] = "none";
        context.Features.Get<IHttpResponseBodyFeature>()?.DisableBuffering();
        return TypedResults.Stream(body => streamer.RunAsync(stream, probe, body, context.RequestAborted), "video/mp4");
    }

    private static Ok<StreamingStatus> Sessions(
        VideoStreamer streamer,
        VideoStreams streams,
        IOptionsMonitor<SfeOptions> options
    ) =>
        TypedResults.Ok(
            new StreamingStatus(
                streamer.IsAvailable,
                streamer.Mode,
                options.CurrentValue.Streaming.MaxStreams,
                streams.List()
            )
        );
}
