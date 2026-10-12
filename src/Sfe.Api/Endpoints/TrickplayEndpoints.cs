using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.Net.Http.Headers;
using Sfe.Api.Security;
using Sfe.Core.Access;
using Sfe.Core.Trickplay;

namespace Sfe.Api.Endpoints;

/// <param name="Paths">Videos to look up, relative to the library root.</param>
public sealed record TrickplayStatesRequest(IReadOnlyList<string>? Paths);

/// <param name="Running">The background job is busy (see the status's state).</param>
/// <param name="Files">States by path, for the videos waiting or being processed only.</param>
public sealed record TrickplayStates(bool Running, IReadOnlyDictionary<string, TrickplayFileState> Files);

public static class TrickplayEndpoints
{
    private const int DefaultActivityLimit = 100;
    private const int MaxActivityLimit = 1000;

    public static RouteGroupBuilder MapTrickplayEndpoints(this RouteGroupBuilder api)
    {
        var trickplay = api.MapGroup("/trickplay").WithTags("Trickplay");

        trickplay.MapGet("/", (TrickplayService service) => TypedResults.Ok(service.Status))
            .RequireRight(Rights.ManageTrickplay, Rights.TrickplaySettings)
            .WithName("GetTrickplayStatus")
            .WithSummary("State of the background trickplay generation.");

        trickplay.MapPost("/", Check)
            .RequireRight(Rights.ManageTrickplay)
            .WithName("CheckTrickplay")
            .WithSummary("Looks for videos without trickplay in the configured folders now.");

        trickplay.MapGet("/activity", (TrickplayService service, int? limit) =>
                TypedResults.Ok(service.GetActivity(Math.Clamp(limit ?? DefaultActivityLimit, 1, MaxActivityLimit))))
            .RequireRight(Rights.ManageTrickplay)
            .WithName("GetTrickplayActivity")
            .WithSummary("Videos being processed, the first waiting ones (`limit`, default 100) and the last ones finished.");

        trickplay.MapPost("/states", States)
            .RequireRight(Rights.ViewFiles)
            .WithName("GetTrickplayStates")
            .WithSummary("Which of the given videos are waiting for their trickplay or being processed.")
            .WithDescription($"""
                Takes up to {TrickplayService.MaxStatePaths} library paths; videos with nothing to do are left out of
                `files`. `running` tells whether the job is busy at all: the client may check less often when not.
                """);

        trickplay.MapGet("/files/{**path}", Get)
            .RequireRight(Rights.ViewFiles)
            .WithName("GetTrickplay")
            .WithSummary("Trickplay of a video: its description, or one of its sheets with `?sheet=n`.")
            .WithDescription("""
                404 when the video has no trickplay yet. A video of the configured folders is then queued, ahead of
                the background pass, unless `queue=false` (a thumbnail preview only shows what exists). Sheets are JPEG images; their URL should carry the description's `version`
                (e.g. `&v=`): they are cached by the browser.
                """);

        return api;
    }

    /// <summary>409 when trickplay is turned off or ffmpeg can't be found (see the status's state).</summary>
    private static Results<Accepted<TrickplayStatus>, Conflict<TrickplayStatus>> Check(TrickplayService service) =>
        service.RequestPass()
            ? TypedResults.Accepted("/api/trickplay", service.Status)
            : TypedResults.Conflict(service.Status);

    private static Results<Ok<TrickplayStates>, ValidationProblem> States(TrickplayService service, TrickplayStatesRequest? request)
    {
        var paths = request?.Paths ?? [];
        if (paths.Count > TrickplayService.MaxStatePaths)
            return TypedResults.ValidationProblem(new Dictionary<string, string[]> { ["paths"] = [$"At most {TrickplayService.MaxStatePaths} paths."] });

        return TypedResults.Ok(new TrickplayStates(service.Status.State == TrickplayState.Running, service.GetStates(paths)));
    }

    private static Results<Ok<TrickplayManifest>, PhysicalFileHttpResult, NotFound> Get(
        TrickplayService service,
        HttpContext context,
        string path,
        int? sheet = null,
        bool queue = true)
    {
        if (sheet is null)
            return service.Get(path, queue) is { } manifest ? TypedResults.Ok(manifest) : TypedResults.NotFound();

        var file = service.GetSheet(path, sheet.Value);
        if (file is null)
            return TypedResults.NotFound();

        // Unlike thumbnails, sheets can be cached: a regenerated trickplay has a new version, hence new URLs.
        context.Response.Headers[HeaderNames.CacheControl] = "private, max-age=604800";
        return TypedResults.PhysicalFile(file, "image/jpeg", lastModified: File.GetLastWriteTimeUtc(file));
    }
}
