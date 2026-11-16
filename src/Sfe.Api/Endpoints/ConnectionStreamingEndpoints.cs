using Microsoft.AspNetCore.Http.Features;
using Microsoft.Net.Http.Headers;
using Otools.Net.Sfe.Api.Security;
using Otools.Net.Sfe.Core.Access;
using Otools.Net.Sfe.Core.Connections;
using Otools.Net.Sfe.Core.Connections.Previews;
using Otools.Net.Sfe.Core.Library;
using Otools.Net.Sfe.Core.Streaming;

namespace Otools.Net.Sfe.Api.Endpoints;

/// <summary>
/// Videos of a connection converted while they play (agents/docs/features/video-streams.md): the file is downloaded
/// whole into the server's content cache first (once, whoever asks), then ffmpeg reads it there, only when its bytes
/// are a video (<see cref="Core.Thumbnails.Ffmpeg.ConnectionInputAsync"/>).
/// </summary>
internal static class ConnectionStreamingEndpoints
{
    private const string ProbeDescription = """
        `api/streaming/probe` for a connection's video. The first call downloads the file into the server's cache
        (it answers when the file is there): 413 (`too-large`) over the cache's file limit, 429
        (`connection-cache-busy`) while the server downloads as many files as it may. 404 when streaming is off,
        ffmpeg is missing, or the file's bytes are not a video.
        """;

    private const string PlayDescription = """
        `api/streaming/play` for a connection's video, from the server's cache. `player` names the player: a new
        request with the same `player` stops its earlier stream. 503 while the server converts
        `Streaming:MaxStreams` videos already.
        """;

    public static RouteGroupBuilder MapConnectionStreamingEndpoints(this RouteGroupBuilder api, RouteGroupBuilder group)
    {
        group
            .MapGet("/streaming/probe", Probe)
            .RequireRateLimiting(RequestLimits.Media)
            .RequireRight(Rights.UseConnections)
            .FolderScoped("files.stream and files.view; 404 for a connection the subject may not use")
            .WithName("ProbeConnectionVideoStream")
            .WithSummary("Whether a video of a connection can be played converted by the server, and its length.")
            .WithDescription(ProbeDescription);

        group
            .MapGet("/streaming/play", Play)
            .RequireRateLimiting(RequestLimits.Media)
            .RequireRight(Rights.UseConnections)
            .FolderScoped("files.stream and files.view; 404 for a connection the subject may not use")
            .WithName("PlayConnectionVideoStream")
            .WithSummary("A video of a connection converted by ffmpeg while it plays, from `t` seconds.")
            .WithDescription(PlayDescription);

        return api;
    }

    private static async Task<IResult> Probe(
        int id,
        string path,
        [AsParameters] ConnectionFileServices files,
        [AsParameters] ConnectionPreviewServices previews,
        CancellationToken cancellationToken
    )
    {
        var (ask, open, answer) = await OpenAsync(id, path, files, previews, cancellationToken);
        if (ask is null)
            return answer!;
        return await files.CatchAsync(open!, async () =>
        {
            using var cached = await previews.Previews.ContentAsync(ask, cancellationToken);
            if (await previews.Streamer.ProbeCachedAsync(cached, cancellationToken) is not { } probe)
                return TypedResults.NotFound();
            var copied = previews.Options.CurrentValue.Streaming.CopyVideo && probe.BrowserPictures;
            return TypedResults.Ok(new StreamProbe(probe.Duration, !copied));
        });
    }

    private static async Task<IResult> Play(
        int id,
        [AsParameters] ConnectionPlayQuery query,
        [AsParameters] ConnectionFileServices files,
        [AsParameters] ConnectionPreviewServices previews,
        CancellationToken cancellationToken
    )
    {
        var (ask, open, answer) = await OpenAsync(id, query.Path, files, previews, cancellationToken);
        if (ask is null)
            return answer!;
        var state = await files.Current.GetAsync();
        var viewer = new StreamViewer(
            state.Tenant?.Name ?? string.Empty,
            state.User is { } user ? user.DisplayName ?? user.UserName : null,
            files.Context.Connection.RemoteIpAddress?.ToString()
        );
        // The player's id, kept apart per account: one viewer can't stop another's stream.
        var client = string.IsNullOrEmpty(query.Player) ? null : $"{state.User?.Id}:{query.Player}";
        var start = double.IsFinite(query.T) ? query.T : 0;
        return await files.CatchAsync(open!, async () =>
        {
            var cached = await previews.Previews.ContentAsync(ask, cancellationToken);
            if (await previews.Streamer.ProbeCachedAsync(cached, cancellationToken) is not { } probe)
            {
                cached.Dispose();
                return TypedResults.NotFound();
            }
            var asked = (probe, start, LabelOf(open!, ask.Item));
            if (previews.Streamer.TryBeginCached(cached, asked, viewer, client) is not { } stream)
                return Busy();
            var context = files.Context;
            context.Response.Headers[HeaderNames.CacheControl] = "no-store";
            context.Response.Headers[HeaderNames.AcceptRanges] = "none";
            context.Features.Get<IHttpResponseBodyFeature>()?.DisableBuffering();
            return TypedResults.Stream(
                body => previews.Streamer.RunAsync(stream, probe, body, context.RequestAborted),
                "video/mp4"
            );
        });
    }

    /// <summary>The video, checked (streaming on, rights at its path and where it is, a video by its name).</summary>
    private static async Task<(PreviewAsk? Ask, OpenConnection? Open, IResult? Answer)> OpenAsync(
        int id,
        string path,
        ConnectionFileServices files,
        ConnectionPreviewServices previews,
        CancellationToken cancellationToken
    )
    {
        if (!previews.Streamer.IsAvailable || RemotePaths.Normalize(path) is not { Length: > 0 } normalized)
            return (null, null, TypedResults.NotFound());
        if (previews.Classifier.Classify(Path.GetExtension(normalized)) != ItemKind.Video)
            return (null, null, TypedResults.NotFound());
        var (open, answer) = await files.OpenAsync(id, Rights.Stream, cancellationToken);
        if (open is null)
            return (null, null, answer);
        if (!(await files.Current.GetAsync()).Has(Rights.ViewFiles))
            return (null, null, TypedResults.Forbid());
        if (OpenConnection.AnswerOf(Stricter(open, normalized)) is { } refused)
            return (null, null, refused);

        var request = new PreviewRequest(files, previews, open, normalized, Rights.Stream, Fresh: true);
        var (ask, problem) = await ConnectionPreviewFiles.OpenAsync(request, cancellationToken);
        return (ask, open, problem);
    }

    private static PathVerdict Stricter(OpenConnection open, string path)
    {
        var (stream, view) = (open.Check(path, Rights.Stream), open.Check(path, Rights.ViewFiles));
        return stream > view ? stream : view;
    }

    /// <summary>
    /// What the list of streams shows: never a path of someone's own connection (administrators read it).
    /// </summary>
    private static string LabelOf(OpenConnection open, RemoteItem item) =>
        open.Connection.Level == ConnectionLevel.Account
            ? "(a file of a private connection)"
            : $"{open.Connection.Name}: {item.Path}";

    private static IResult Busy() =>
        TypedResults.Problem(
            "The server is converting as many videos as it may at once: try again when one has ended.",
            statusCode: StatusCodes.Status503ServiceUnavailable
        );
}
