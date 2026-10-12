using Microsoft.AspNetCore.Http.HttpResults;
using Sfe.Api.Security;
using Sfe.Core.Access;
using Sfe.Core.Indexing;
using Sfe.Core.Paths;
using Sfe.Core.Settings;

namespace Sfe.Api.Endpoints;

/// <param name="Path">Folder to index, relative to the library root; empty for the whole library.</param>
public sealed record ScanRequest(string? Path);

/// <param name="Path">The folder, relative to the library root.</param>
/// <param name="Excluded">The folder is in the scans' excluded folders (<c>Scan:ExcludedFolders</c>).</param>
public sealed record IndexedFolder(string Path, bool Indexed, int Directories, int Files, int TaggedFiles, bool Excluded);

/// <param name="Directories">Folders removed from the index (the folder and its sub-folders).</param>
/// <param name="TaggedFiles">Of the files, those kept as missing files with their tags (when relinking is on).</param>
/// <param name="Excluded">The folder was added to the excluded folders.</param>
public sealed record RemovedFromIndex(string Path, int Directories, int Files, int TaggedFiles, bool Excluded);

public static class ScanEndpoints
{
    public static RouteGroupBuilder MapScanEndpoints(this RouteGroupBuilder api)
    {
        var scan = api.MapGroup("/scan").WithTags("Index");

        scan.MapGet("/", (ScanCoordinator coordinator) => TypedResults.Ok(coordinator.Status))
            .RequireRight(Rights.Browse)
            .WithName("GetScanStatus")
            .WithSummary("Progress of the current or last index scan.");

        scan.MapPost("/", Start)
            .RequireRight(Rights.Scan)
            .WithName("StartScan")
            .WithSummary("Starts indexing a folder recursively in the background.");

        scan.MapDelete("/", (ScanCoordinator coordinator) =>
                coordinator.Cancel() ? Results.Accepted("/api/scan", coordinator.Status) : Results.Conflict(coordinator.Status))
            .RequireRight(Rights.Scan)
            .WithName("CancelScan")
            .WithSummary("Cancels the running scan.");

        var index = api.MapGroup("/index").WithTags("Index");

        index.MapGet("/folder", GetFolder)
            .RequireRight(Rights.RemoveFromIndex)
            .WithName("GetIndexedFolder")
            .WithSummary("What the index holds of a folder and its sub-folders: counts of folders, files and tagged files.");

        index.MapDelete("/folder", RemoveFolder)
            .RequireRight(Rights.RemoveFromIndex)
            .WithName("RemoveFolderFromIndex")
            .WithSummary("Removes a folder and its sub-folders from the index; the files stay on the disk.")
            .WithDescription("""
                Tagged files are kept as missing files with their tags (unless relinking is off): scanning the folder
                again gives the tags back. With `exclude=true` the folder is also added to `Scan:ExcludedFolders`, so
                that scans of its parents skip it (not for the library root). 409 while a scan runs.
                """);

        return api;
    }

    private static async Task<Results<Ok<IndexedFolder>, ValidationProblem>> GetFolder(
        DirectoryIndexer indexer, LibraryPaths paths, Microsoft.Extensions.Options.IOptionsMonitor<Sfe.Core.Options.SfeOptions> options, string? path, CancellationToken cancellationToken)
    {
        var relative = LibraryPaths.NormalizeRelative(path);
        if (relative is null)
            return InvalidPath();
        var stats = await indexer.GetStatsAsync(paths.Resolve(relative), cancellationToken);
        var excluded = options.CurrentValue.Scan.ExcludedFolders.Contains(relative, LibraryPaths.PathComparer);
        return TypedResults.Ok(new IndexedFolder(relative, stats.Indexed, stats.Directories, stats.Files, stats.TaggedFiles, excluded));
    }

    private static async Task<Results<Ok<RemovedFromIndex>, Conflict<ScanStatus>, ValidationProblem>> RemoveFolder(
        ScanCoordinator coordinator, SettingsService settings, string? path, bool exclude = false, CancellationToken cancellationToken = default)
    {
        var relative = LibraryPaths.NormalizeRelative(path);
        if (relative is null)
            return InvalidPath();
        if (await coordinator.RemoveFromIndexAsync(relative, cancellationToken) is not { } removed)
            return TypedResults.Conflict(coordinator.Status);

        // The root can't be excluded: scans would have nothing left.
        var excluded = exclude && relative.Length > 0;
        if (excluded)
            await settings.ExcludeFolderAsync(relative, cancellationToken: cancellationToken);
        return TypedResults.Ok(new RemovedFromIndex(relative, removed.Directories, removed.Files, removed.TaggedFiles, excluded));
    }

    private static ValidationProblem InvalidPath() =>
        TypedResults.ValidationProblem(new Dictionary<string, string[]> { ["path"] = ["Invalid library path."] });

    private static Results<Accepted<ScanStatus>, Conflict<ScanStatus>, ValidationProblem> Start(ScanCoordinator coordinator, ScanRequest? request)
    {
        var path = LibraryPaths.NormalizeRelative(request?.Path);
        if (path is null)
            return TypedResults.ValidationProblem(new Dictionary<string, string[]> { ["path"] = ["Invalid library path."] });

        return coordinator.TryStart(path)
            ? TypedResults.Accepted("/api/scan", coordinator.Status)
            : TypedResults.Conflict(coordinator.Status);
    }
}
