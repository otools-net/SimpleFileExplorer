using Microsoft.AspNetCore.Http.HttpResults;
using Otools.Net.Sfe.Api.Security;
using Otools.Net.Sfe.Core.Access;
using Otools.Net.Sfe.Core.FileTracking;

namespace Otools.Net.Sfe.Api.Endpoints;

public static class FileTrackingEndpoints
{
    private const string FindDuplicatesDescription =
        "Only files of `path` and below are compared. Pass the `next` value of a page as `after` to get the "
        + "next one; the first page also carries totals.";

    private const string RemoveDuplicateDescription =
        "The two files are compared byte for byte first, and paths leading to the same file (folder links) "
        + "are refused. 409 when turned off (`FileTracking:Duplicates:AllowRemove`) "
        + "or when the file cannot be deleted.";

    private const string FindSimilarVideosDescription =
        "Only groups with a video in `path` or below are listed. Each video's `match` is its share of matching "
        + "frames with the first video of the group. Pass the `next` value of a page as `after` to get the "
        + "next one; the first page also carries totals. Carries the comparison's status: "
        + "start one with `POST /api/similar-videos`.";

    private const string ListMissingFilesDescription =
        "Scans relink them on their own when exactly one likely new place is indexed; "
        + "the others wait here until relinked by hand, forgotten, or kept longer than the retention.";

    public static RouteGroupBuilder MapFileTrackingEndpoints(this RouteGroupBuilder api)
    {
        MapDuplicates(api);

        MapMissing(api);

        return api;
    }

    /// <summary>Duplicate files and similar videos.</summary>
    private static void MapDuplicates(RouteGroupBuilder api)
    {
        api.MapGet("/duplicates", Duplicates)
            .RequireRight(Rights.Duplicates)
            .FolderScoped("404 for a scope the subject doesn't see; groups of the files it sees only")
            .FolderScoped("404 for a scope the subject doesn't see; groups of the files it sees only")
            .WithName("FindDuplicates")
            .WithTags("File tracking")
            .WithSummary("Groups of indexed files with the same size and fingerprint, biggest first.")
            .WithDescription(FindDuplicatesDescription);

        api.MapPost("/duplicates/remove", RemoveDuplicate)
            .RequireRight(Rights.RemoveDuplicates)
            .FolderScoped(
                "tracking.duplicatesRemove at the copy deleted (its folder must be writable), seeing the copy kept"
            )
            .FolderScoped(
                "tracking.duplicatesRemove at the copy deleted (its folder must be writable), seeing the copy kept"
            )
            .WithName("RemoveDuplicate")
            .WithTags("File tracking")
            .WithSummary("Deletes a copy from the disk after adding its tags to an identical copy that is kept.")
            .WithDescription(RemoveDuplicateDescription);

        api.MapGet("/similar-videos", SimilarVideos)
            .RequireRight(Rights.SimilarVideos)
            .FolderScoped("404 for a scope the subject doesn't see; only the videos it sees, groups of two or more")
            .FolderScoped("404 for a scope the subject doesn't see; only the videos it sees, groups of two or more")
            .WithName("FindSimilarVideos")
            .WithTags("File tracking")
            .WithSummary(
                "Groups of videos with the same pictures but other bytes (other resolution, bitrate, codec, trimmed), "
                    + "from the last comparison."
            )
            .WithDescription(FindSimilarVideosDescription);

        api.MapPost("/similar-videos", (SimilarVideoFinder finder) =>
            {
                finder.TryStart();
                return TypedResults.Accepted("similar-videos", finder.Status);
            })
            .RequireRight(Rights.SimilarVideos)
            .WithName("CompareVideos")
            .WithTags("File tracking")
            .WithSummary(
                "Compares the fingerprints of the indexed videos in the background (made with their trickplay); "
                    + "does nothing while a comparison runs."
            );
    }

    /// <summary>Tagged files a scan no longer found, and relinking them.</summary>
    private static void MapMissing(RouteGroupBuilder api)
    {
        var missing = api.MapGroup("/missing-files").WithTags("File tracking");

        missing
            .MapGet(
                "/",
                async (
                    RelinkService service,
                    CurrentAccess current,
                    CancellationToken cancellationToken,
                    int? after = null,
                    int? limit = null
                ) => await service.ListAsync(after, limit, cancellationToken, (await current.GetAsync()).Visible())
            )
            .RequireRight(Rights.MissingFiles)
            .FolderScoped("only missing files that were, and candidates that are, where the subject sees")
            .WithName("ListMissingFiles")
            .WithSummary("Tagged files a scan no longer found, with the indexed files that may be them.")
            .WithDescription(ListMissingFilesDescription);

        missing
            .MapPost("/relink", (RelinkService service, CancellationToken cancellationToken) =>
                service.RunAsync(cancellationToken)
            )
            .RequireRight(Rights.MissingFiles)
            .WithName("RelinkMissingFiles")
            .WithSummary(
                "Relinks now the missing files that have exactly one likely new place (done after every scan)."
            );

        missing
            .MapPost("/{id:int}/relink", Relink)
            .RequireRight(Rights.MissingFiles)
            .FolderScoped("the missing file was where the subject sees, and it sees the file it becomes")
            .FolderScoped("the missing file was where the subject sees, and it sees the file it becomes")
            .WithName("RelinkMissingFile")
            .WithSummary("Gives the tags of a missing file to a file of the library, and forgets the missing file.");

        MapForget(missing);
    }

    /// <summary>Missing files forgotten: one, or all of them.</summary>
    private static void MapForget(RouteGroupBuilder missing)
    {
        missing
            .MapDelete(
                "/{id:int}",
                async Task<Results<Ok<ForgetResult>, NotFound>> (
                    RelinkService service,
                    CurrentAccess current,
                    int id,
                    CancellationToken cancellationToken
                ) =>
                    await service.ForgetAsync(id, cancellationToken, (await current.GetAsync()).Visible())
                        is var forgotten
                            and > 0
                        ? TypedResults.Ok(new ForgetResult(forgotten))
                        : TypedResults.NotFound()
            )
            .RequireRight(Rights.MissingFiles)
            .FolderScoped("only a missing file that was where the subject sees")
            .WithName("ForgetMissingFile")
            .WithSummary("Forgets a missing file and its tags.");

        missing
            .MapDelete("/", async (RelinkService service, CurrentAccess current, CancellationToken cancellationToken) =>
                TypedResults.Ok(
                    new ForgetResult(
                        await service.ForgetAsync(null, cancellationToken, (await current.GetAsync()).Visible())
                    )
                )
            )
            .RequireRight(Rights.MissingFiles)
            .FolderScoped("only the missing files that were where the subject sees")
            .WithName("ForgetMissingFiles")
            .WithSummary("Forgets every missing file and their tags.");
    }

    private static async Task<Results<Ok<DuplicateResults>, ValidationProblem, NotFound>> Duplicates(
        [AsParameters] FileTrackingServices services,
        DuplicateFinder finder,
        [AsParameters] TrackingPageQuery query,
        CancellationToken cancellationToken
    )
    {
        var (paths, _, _, current) = services;
        var (path, after, limit) = query;
        var state = await current.GetAsync();
        if (state.At(paths, path) is not { IsVisible: true })
            return TypedResults.NotFound();
        return await finder.FindAsync(path, after, limit, cancellationToken, state.Visible()) is { } results
            ? TypedResults.Ok(
                state.Access.FolderPolicy.IsUnrestricted
                    ? results
                    : results with
                    {
                        Groups =
                        [
                            .. results
                                .Groups.Select(g => g with { Files = state.Seen(paths, g.Files) })
                                .Where(g => g.Files.Count > 1),
                        ],
                    }
            )
            : TypedResults.ValidationProblem(
                new Dictionary<string, string[]> { ["path"] = ["Invalid library path or page."] }
            );
    }

    private static async Task<Results<Ok<SimilarVideoResults>, ValidationProblem, NotFound>> SimilarVideos(
        [AsParameters] FileTrackingServices services,
        SimilarVideoFinder finder,
        [AsParameters] TrackingPageQuery query,
        CancellationToken cancellationToken
    )
    {
        var (paths, _, _, current) = services;
        var (path, after, limit) = query;
        var state = await current.GetAsync();
        if (state.At(paths, path) is not { IsVisible: true })
            return TypedResults.NotFound();
        return await finder.FindAsync(path, after, limit, cancellationToken, state.Visible()) is { } results
            ? TypedResults.Ok(
                state.Access.FolderPolicy.IsUnrestricted
                    ? results
                    : results with
                    {
                        Groups =
                        [
                            .. results
                                .Groups.Select(g =>
                                    g with
                                    {
                                        Videos = [.. g.Videos.Where(v => state.Sees(paths, v.File.Path))],
                                    }
                                )
                                .Where(g => g.Videos.Count > 1),
                        ],
                    }
            )
            : TypedResults.ValidationProblem(
                new Dictionary<string, string[]>
                {
                    ["path"] = ["Invalid library path, or page of an older comparison."],
                }
            );
    }

    private static async Task<
        Results<Ok<RemovedDuplicate>, ValidationProblem, ProblemHttpResult, NotFound, ForbidHttpResult>
    > RemoveDuplicate(
        [AsParameters] FileTrackingServices services,
        DuplicateRemover remover,
        RemoveDuplicateRequest request,
        CancellationToken cancellationToken
    )
    {
        var (paths, serverFiles, work, current) = services;
        var state = await current.GetAsync();
        // The copy deleted must be in a folder whose files the subject may change; the one kept, one it sees.
        if (!state.Sees(paths, request.Keep ?? string.Empty))
            return TypedResults.NotFound();
        if (state.Check(paths, request.Path, Rights.RemoveDuplicates) is var verdict and not PathVerdict.Allowed)
            return verdict == PathVerdict.NotFound ? TypedResults.NotFound() : TypedResults.Forbid();
        if (
            paths.TryResolve(request.Path, out var removed)
            && FileEndpoints.ProtectedFromWrites(removed, serverFiles, work)
        )
        {
            return TypedResults.Problem(FileEndpoints.ReadOnlyThroughLink, statusCode: StatusCodes.Status403Forbidden);
        }

        var result = await remover.RemoveAsync(
            request.Path,
            request.Keep,
            FileEndpoints.Actor(state),
            cancellationToken
        );
        return result.Status switch
        {
            DuplicateRemovalStatus.Removed => TypedResults.Ok(
                new RemovedDuplicate(request.Path!, result.Kept!, result.TrashId)
            ),
            DuplicateRemovalStatus.Invalid => TypedResults.ValidationProblem(
                new Dictionary<string, string[]> { [result.Field ?? "path"] = [result.Error!] }
            ),
            _ => TypedResults.Problem(result.Error, statusCode: StatusCodes.Status409Conflict),
        };
    }

    private static async Task<Results<NoContent, NotFound, ValidationProblem>> Relink(
        [AsParameters] FileTrackingServices services,
        RelinkService service,
        int id,
        RelinkRequest request,
        CancellationToken cancellationToken
    )
    {
        var (paths, _, _, current) = services;
        var state = await current.GetAsync();
        var invalid = TypedResults.ValidationProblem(
            new Dictionary<string, string[]>
            {
                ["path"] = [$"“{request.Path}” is not a file of the library that can be indexed."],
            }
        );
        // A file the subject doesn't see is answered as one that isn't there.
        if (!state.Sees(paths, request.Path ?? string.Empty))
            return invalid;
        return await service.RelinkAsync(id, request.Path, cancellationToken, state.Visible()) switch
        {
            RelinkStatus.Relinked => TypedResults.NoContent(),
            RelinkStatus.InvalidPath => invalid,
            _ => TypedResults.NotFound(),
        };
    }
}
