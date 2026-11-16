using Otools.Net.Sfe.Api.Security;
using Otools.Net.Sfe.Core.Access;
using Otools.Net.Sfe.Core.Connections;
using Otools.Net.Sfe.Core.Library;

namespace Otools.Net.Sfe.Api.Endpoints;

/// <summary>
/// Previews of a connection's files (agents/docs/features/connection-previews.md): thumbnails (the provider's, kept in
/// the server's cache, else made by ffmpeg from the content cache), text read whole from the provider, and videos
/// converted while they play from the content cache (<see cref="ConnectionStreamingEndpoints"/>). Opened as every
/// connection route (<see cref="ConnectionFileServices.OpenAsync(int, string, CancellationToken)"/>), each path
/// checked by the connection's folder rules before the provider or the cache is read.
/// </summary>
public static class ConnectionPreviewEndpoints
{
    private const string ThumbnailDescription = """
        The provider's thumbnail when it makes one (asked together with others arriving at the same moment), kept
        in the server's cache under the file's id and version: a file changed at the provider gets a new one. A
        picture the provider doesn't thumbnail (Dropbox: over 20 MB) is made by ffmpeg from the file downloaded into
        the server's content cache, when ffmpeg is there and the file not over the cache's limit; a video only when
        it is in that cache already (played converted). 404 when the file has none.
        """;

    public static RouteGroupBuilder MapConnectionPreviewEndpoints(this RouteGroupBuilder api)
    {
        var connections = api.MapGroup("/connections/{id:int}").WithTags("Connections");

        connections
            .MapGet("/thumbnails/{**path}", Thumbnail)
            .RequireRateLimiting(RequestLimits.Media)
            .RequireRight(Rights.UseConnections)
            .FolderScoped("files.view; 404 for a connection the subject may not use")
            .WithName("GetConnectionThumbnail")
            .WithSummary("JPEG preview of a picture or video of a connection.")
            .WithDescription(ThumbnailDescription);

        connections
            .MapGet("/files/text", ConnectionTextEndpoint.Get)
            .RequireRateLimiting(RequestLimits.Connections)
            .RequireRight(Rights.UseConnections)
            .FolderScoped("files.view; 404 for a connection the subject may not use")
            .WithName("GetConnectionTextFile")
            .WithSummary("A file of a connection as text, read only (its encoding and line ending told).")
            .WithDescription(ConnectionTextEndpoint.Description);

        return api.MapConnectionStreamingEndpoints(connections);
    }

    private static async Task<IResult> Thumbnail(
        int id,
        string path,
        [AsParameters] ConnectionFileServices files,
        [AsParameters] ConnectionPreviewServices previews,
        int size = 256
    )
    {
        var cancellationToken = files.Context.RequestAborted;
        if (RemotePaths.Normalize(path) is not { Length: > 0 } normalized)
            return TypedResults.NotFound();
        if (previews.Classifier.Classify(Path.GetExtension(normalized)) is not (ItemKind.Image or ItemKind.Video))
            return TypedResults.NotFound();
        var (open, answer) = await files.OpenAsync(id, Rights.ViewFiles, cancellationToken);
        if (open is null)
            return answer!;
        if (!open.Capabilities.Thumbnails && !previews.Thumbnails.IsAvailable)
            return TypedResults.NotFound();
        if (OpenConnection.AnswerOf(open.Check(normalized, Rights.ViewFiles)) is { } refused)
            return refused;

        var request = new PreviewRequest(files, previews, open, normalized, Rights.ViewFiles, Fresh: false);
        var (ask, problem) = await ConnectionPreviewFiles.OpenAsync(request, cancellationToken);
        if (ask is null)
            return problem!;
        return await files.CatchAsync(open, async () =>
        {
            var bytes = await previews.Previews.ThumbnailAsync(ask, Math.Clamp(size, 32, 1024), cancellationToken);
            if (bytes is null)
                return TypedResults.NotFound();
            files.Context.Response.Headers.CacheControl = "no-store";
            return TypedResults.File(bytes, "image/jpeg");
        });
    }
}
