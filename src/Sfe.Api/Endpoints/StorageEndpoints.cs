using Microsoft.AspNetCore.Http.HttpResults;
using Otools.Net.Sfe.Api.Security;
using Otools.Net.Sfe.Core.Access;
using Otools.Net.Sfe.Core.Files;
using Otools.Net.Sfe.Core.Paths;

namespace Otools.Net.Sfe.Api.Endpoints;

/// <summary>
/// The space used and free where files are kept, for the sidebar's bar: the drive of the library folder shown, which a
/// junction or a link inside the library may put on another volume (numbers only, never its path, which space
/// administrators don't see), and a connection's account as its provider reports it. Read at most every
/// <see cref="StorageUsageCache.Lifetime"/> per volume folder (<see cref="DiskSpace.VolumeFolder"/>) or connection.
/// </summary>
public static class StorageEndpoints
{
    /// <summary>A drive not answering in this time (a share gone) is not reported, the request not held.</summary>
    private static readonly TimeSpan DiskWait = TimeSpan.FromSeconds(5);

    private const string LibraryDescription = """
        `path`: the library folder shown (empty: the library's folder); a junction, a mounted folder or a link on
        its way is measured on the volume it leads to. A folder the session may not browse, or that is not there,
        gives the library's folder.
        """;

    private const string Description = """
        `used`, `total` and `free` in bytes; `reported` false (the numbers null) when the drive or the provider
        doesn't tell. A provider may give `used` without `total` (no limit known). Read again at most every two
        minutes.
        """;

    public static RouteGroupBuilder MapStorageEndpoints(this RouteGroupBuilder api)
    {
        api.MapGet("/storage", Library)
            .RequireRight(Rights.Browse)
            .FolderScoped("library.browse at the folder; a folder the session may not browse gives the library's")
            .WithTags("Library")
            .WithName("GetLibraryStorage")
            .WithSummary("The space used and free on the drive holding a library folder.")
            .WithDescription(LibraryDescription + Description);

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
        CurrentAccess current,
        StorageUsageCache cache,
        CancellationToken cancellationToken,
        string? path = null
    )
    {
        // A library whose drive did not answer is not asked again for a while: each read would hold a thread.
        var stalled = "library-stalled:" + paths.Root;
        if (cache.Find(stalled) is { } notAnswering)
            return TypedResults.Ok(notAnswering);
        var folder = await ShownFolderAsync(paths, current, path);
        try
        {
            var usage = await Task.Run(
                    () =>
                    {
                        var volume = DiskSpace.VolumeFolder(paths.Root, folder);
                        var key = "library:" + volume;
                        return cache.Find(key) ?? cache.Keep(key, DiskSpace.Of(volume));
                    },
                    cancellationToken
                )
                .WaitAsync(DiskWait, cancellationToken);
            return TypedResults.Ok(usage);
        }
        catch (TimeoutException)
        {
            return TypedResults.Ok(cache.Keep(stalled, StorageUsage.NotReported));
        }
    }

    /// <summary>The full path of the folder asked for, or the library's when the session may not browse it.</summary>
    private static async Task<string> ShownFolderAsync(LibraryPaths paths, CurrentAccess current, string? path)
    {
        if (
            !paths.TryResolve(path, out var full)
            || paths.ToRelative(full) is not { Length: > 0 } canonical
            || (await current.GetAsync()).Check(paths, canonical, Rights.Browse) != PathVerdict.Allowed
        )
        {
            return paths.Root;
        }
        return full;
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
