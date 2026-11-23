using Microsoft.AspNetCore.Http.HttpResults;
using Otools.Net.Sfe.Api.Security;
using Otools.Net.Sfe.Core.Access;
using Otools.Net.Sfe.Core.Files;
using Otools.Net.Sfe.Core.Paths;

namespace Otools.Net.Sfe.Api.Endpoints;

/// <summary>
/// The space used and free where files are kept, for the sidebar's bar: the drive of the library's folder (numbers
/// only, never its path, which space administrators don't see) and a connection's account as its provider reports
/// it. Read at most every <see cref="StorageUsageCache.Lifetime"/> per drive or connection.
/// </summary>
public static class StorageEndpoints
{
    /// <summary>A drive that doesn't answer in this time (a share gone) is not reported, the request not held.</summary>
    private static readonly TimeSpan DiskWait = TimeSpan.FromSeconds(5);

    private const string Description = """
        `used`, `total` and `free` in bytes; `reported` false (the numbers null) when the drive or the provider
        doesn't tell. A provider may give `used` without `total` (no limit known). Read again at most every two
        minutes.
        """;

    public static RouteGroupBuilder MapStorageEndpoints(this RouteGroupBuilder api)
    {
        api.MapGet("/storage", Library)
            .RequireRight(Rights.Browse)
            .WithTags("Library")
            .WithName("GetLibraryStorage")
            .WithSummary("The space used and free on the drive holding the library's folder.")
            .WithDescription(Description);

        api.MapGet("/connections/{id:int}/storage", Connection)
            .RequireRateLimiting(RequestLimits.Connections)
            .RequireRight(Rights.UseConnections)
            .FolderScoped("library.browse; 404 for a connection the subject may not use")
            .Produces<StorageUsage>()
            .WithTags("Connections")
            .WithName("GetConnectionStorage")
            .WithSummary("The space used and free in a connection's account, as its provider reports it.")
            .WithDescription(Description);

        return api;
    }

    private static async Task<Ok<StorageUsage>> Library(
        LibraryPaths paths,
        StorageUsageCache cache,
        CancellationToken cancellationToken
    )
    {
        var key = "library:" + paths.Root;
        if (cache.Find(key) is { } known)
            return TypedResults.Ok(known);
        StorageUsage usage;
        try
        {
            usage = await Task.Run(() => DiskSpace.Of(paths.Root), cancellationToken)
                .WaitAsync(DiskWait, cancellationToken);
        }
        catch (TimeoutException)
        {
            usage = StorageUsage.NotReported;
        }
        return TypedResults.Ok(cache.Keep(key, usage));
    }

    private static async Task<IResult> Connection(
        int id,
        [AsParameters] ConnectionFileServices files,
        StorageUsageCache cache,
        CancellationToken cancellationToken
    )
    {
        var (open, answer) = await files.OpenAsync(id, Rights.Browse, cancellationToken);
        if (open is null)
            return answer!;
        var key = "connection:" + id;
        if (cache.Find(key) is { } known)
            return TypedResults.Ok(known);
        if (!open.Capabilities.FreeSpace)
            return TypedResults.Ok(cache.Keep(key, StorageUsage.NotReported));

        return await files.RunAsync(open, async storage =>
        {
            var usage = await storage.SpaceAsync(cancellationToken) is { } space
                ? StorageUsage.OfAccount(space.Used, space.Total)
                : StorageUsage.NotReported;
            return TypedResults.Ok(cache.Keep(key, usage));
        });
    }
}
