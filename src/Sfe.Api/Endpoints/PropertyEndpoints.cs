using Microsoft.AspNetCore.Http.HttpResults;
using Sfe.Api.Security;
using Sfe.Core.Access;
using Sfe.Core.Files;
using Sfe.Core.Library;
using Sfe.Core.Paths;
using Sfe.Core.Tagging;

namespace Sfe.Api.Endpoints;

/// <param name="Item">As listings show it, with what the index knows (tags, resolution, duration).</param>
/// <param name="CanChecksum">The caller may read the file whole for a checksum (right `files.download`).</param>
public sealed record ItemProperties(LibraryItem Item, ItemDetails Details, bool CanChecksum);

public sealed record CountRequest(string? Path);

public sealed record ChecksumRequest(string? Path, ChecksumAlgorithm Algorithm = ChecksumAlgorithm.Sha256);

/// <summary>
/// Properties of a file or folder (agents/docs/features/properties.md): details from the disk, a folder's totals from
/// the index, and jobs for what reads the disk at length (a folder counted, a checksum).
/// </summary>
public static class PropertyEndpoints
{
    public static RouteGroupBuilder MapPropertyEndpoints(this RouteGroupBuilder api)
    {
        var properties = api.MapGroup("/files/properties").WithTags("Files");

        properties.MapGet("", Get)
            .RequireRight(Rights.Browse)
            .FolderScoped("files.browse at the item; files.download says whether a checksum is offered")
            .WithName("GetProperties")
            .WithSummary("A file's or folder's properties: dates, size, attributes, and what the index knows of it.");
        properties.MapGet("/totals", Totals)
            .RequireRight(Rights.Browse)
            .FolderScoped("files.browse at the folder; only the files and folders the caller sees are counted")
            .RequireRateLimiting(RequestLimits.Media)
            .WithName("GetFolderTotals")
            .WithSummary("How many files and folders, and how many bytes, the index has below a folder.");
        properties.MapPost("/count", Count)
            .RequireRight(Rights.Browse)
            .FolderScoped("files.browse at the folder and at each folder counted (the others are left out)")
            .WithName("CountFolder")
            .WithSummary("Counts a folder's files, folders and bytes on the disk, in the background.")
            .WithDescription($"""
                Answers the job, followed with `GET jobs/<id>`. Links are not followed; at most
                {PropertyJobs.MaxCountedEntries:N0} entries (`capped`). One count per caller at a time: a new one stops the previous.
                """);
        properties.MapPost("/checksum", Checksum)
            .RequireRight(Rights.Download)
            .FolderScoped("files.download at the file")
            .WithName("ComputeChecksum")
            .WithSummary("Reads a file whole for its checksum (`sha256`, `sha1` or `md5`), in the background.")
            .WithDescription("Answers the job, followed with `GET jobs/<id>`. One checksum per caller at a time: a new one stops the previous.");
        properties.MapGet("/jobs/{id:regex(^[0-9a-f]{{32}}$)}", GetJob)
            .RequireRight(Rights.Browse)
            .NotLibraryPaths()
            .WithName("GetPropertyJob")
            .WithSummary("One of the caller's counts or checksums: its progress, then its result.");
        properties.MapDelete("/jobs/{id:regex(^[0-9a-f]{{32}}$)}", CancelJob)
            .RequireRight(Rights.Browse)
            .NotLibraryPaths()
            .WithName("CancelPropertyJob")
            .WithSummary("Stops one of the caller's counts or checksums.");
        return api;
    }

    private static async Task<Results<Ok<ItemProperties>, NotFound>> Get(
        Properties properties, LibraryBrowser browser, TagService tags, LibraryPaths paths, ServerFiles serverFiles, WorkFolder work, CurrentAccess current,
        ILogger<Properties> logger, string? path, CancellationToken cancellationToken)
    {
        var state = await current.GetAsync();
        if (Find(state, paths, serverFiles, work, path) is not { } found
            || properties.Describe(found.Path, found.Full) is not { } details
            || browser.ToItem(details.IsFolder ? new DirectoryInfo(found.Full) : new FileInfo(found.Full)) is not { } item)
            return TypedResults.NotFound();
        try
        {
            item = (await tags.AttachIndexDataAsync([item], cancellationToken))[0];
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            // The disk's properties without the index's.
            logger.LogWarning(ex, "Cannot read what the index knows of {Path}", found.Path);
        }
        if (found.Path.Length == 0)
            item = item with { Name = paths.RootName };
        return TypedResults.Ok(new ItemProperties(item, details,
            !details.IsFolder && state.Check(paths, found.Path, Rights.Download) == PathVerdict.Allowed));
    }

    private static async Task<Results<Ok<FolderTotals>, NotFound>> Totals(
        Properties properties, LibraryPaths paths, ServerFiles serverFiles, WorkFolder work, CurrentAccess current, string? path, CancellationToken cancellationToken)
    {
        var state = await current.GetAsync();
        if (Find(state, paths, serverFiles, work, path) is not { } found || !Directory.Exists(found.Full))
            return TypedResults.NotFound();
        return TypedResults.Ok(await properties.IndexedTotalsAsync(found.Full, state.Visible(), cancellationToken));
    }

    private static async Task<Results<Ok<PropertyJobView>, NotFound>> Count(
        PropertyJobs jobs, LibraryPaths paths, ServerFiles serverFiles, WorkFolder work, CurrentAccess current, HttpContext context, CountRequest request)
    {
        var state = await current.GetAsync();
        if (Find(state, paths, serverFiles, work, request.Path) is not { } found || !Directory.Exists(found.Full))
            return TypedResults.NotFound();
        return TypedResults.Ok(jobs.StartCount(RequestLimits.Caller(context), found.Path, found.Full, new PropertyAccess(state, paths, serverFiles, work)));
    }

    private static async Task<Results<Ok<PropertyJobView>, NotFound, ForbidHttpResult>> Checksum(
        PropertyJobs jobs, LibraryPaths paths, ServerFiles serverFiles, WorkFolder work, CurrentAccess current, HttpContext context, ChecksumRequest request)
    {
        var state = await current.GetAsync();
        if (Find(state, paths, serverFiles, work, request.Path) is not { } found || !File.Exists(found.Full))
            return TypedResults.NotFound();
        if (state.Check(paths, found.Path, Rights.ViewFiles) != PathVerdict.Allowed || state.Check(paths, found.Path, Rights.Download) != PathVerdict.Allowed)
            return TypedResults.Forbid();
        return TypedResults.Ok(jobs.StartChecksum(RequestLimits.Caller(context), found.Path, found.Full, request.Algorithm));
    }

    private static Results<Ok<PropertyJobView>, NotFound> GetJob(PropertyJobs jobs, HttpContext context, string id) =>
        jobs.Get(RequestLimits.Caller(context), id) is { } job ? TypedResults.Ok(job) : TypedResults.NotFound();

    private static Results<NoContent, NotFound> CancelJob(PropertyJobs jobs, HttpContext context, string id) =>
        jobs.Cancel(RequestLimits.Caller(context), id) ? TypedResults.NoContent() : TypedResults.NotFound();

    /// <summary>The item, canonical and full, when the caller sees it and it isn't one of the server's own files.</summary>
    private static (string Path, string Full)? Find(AccessState state, LibraryPaths paths, ServerFiles serverFiles, WorkFolder work, string? path)
    {
        if (!paths.TryResolve(path, out var full) || paths.ToRelative(full) is not { } canonical
            || (!File.Exists(full) && !Directory.Exists(full)) || FileEndpoints.Protected(full, serverFiles, work)
            || state.Check(paths, canonical, Rights.Browse) != PathVerdict.Allowed)
            return null;
        return (canonical, full);
    }

    /// <summary>The caller's access when the count started.</summary>
    private sealed class PropertyAccess(AccessState state, LibraryPaths paths, ServerFiles serverFiles, WorkFolder work) : IPropertyAccess
    {
        public bool Sees(string path) => state.Check(paths, path, Rights.Browse) == PathVerdict.Allowed;

        public bool IsProtectedReal(string realPath) => serverFiles.ContainsReal(realPath) || work.Contains(realPath);
    }
}
