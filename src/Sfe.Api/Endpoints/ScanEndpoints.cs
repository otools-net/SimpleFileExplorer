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

        scan.MapGet("/", ScanStatusOf)
            .RequireRight(Rights.Browse)
            .FolderScoped("the folders scanned are left out when the subject doesn't see them")
            .WithName("GetScanStatus")
            .WithSummary("Progress of the current or last index scan.");

        scan.MapPost("/", Start)
            .RequireRight(Rights.Scan)
            .FolderScoped("index.scan at the folder")
            .FolderScoped("index.scan at the folder")
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
            .FolderScoped("index.remove at the folder and everywhere below it")
            .FolderScoped("index.remove at the folder and everywhere below it")
            .WithName("GetIndexedFolder")
            .WithSummary("What the index holds of a folder and its sub-folders: counts of folders, files and tagged files.");

        index.MapDelete("/folder", RemoveFolder)
            .RequireRight(Rights.RemoveFromIndex)
            .FolderScoped("index.remove at the folder and everywhere below it")
            .FolderScoped("index.remove at the folder and everywhere below it")
            .WithName("RemoveFolderFromIndex")
            .WithSummary("Removes a folder and its sub-folders from the index; the files stay on the disk.")
            .WithDescription("""
                Tagged files are kept as missing files with their tags (unless relinking is off): scanning the folder
                again gives the tags back. With `exclude=true` the folder is also added to `Scan:ExcludedFolders`, so
                that scans of its parents skip it (not for the library root). 409 while a scan runs.
                """);

        return api;
    }

    /// <summary>The scan's progress; the folders it is on are left out for a subject that doesn't see them.</summary>
    private static async Task<Ok<ScanStatus>> ScanStatusOf(ScanCoordinator coordinator, LibraryPaths paths, CurrentAccess current)
    {
        var status = coordinator.Status;
        var state = await current.GetAsync();
        if (state.Access.FolderPolicy.IsUnrestricted)
            return TypedResults.Ok(status);
        return TypedResults.Ok(status with
        {
            Path = status.Path is { } scanned && state.At(paths, scanned) is { IsVisible: true } ? scanned : null,
            CurrentDirectory = status.CurrentDirectory is { } directory && state.Sees(paths, directory) ? directory : null,
        });
    }

    /// <summary>
    /// Removing a tree from the index (or reading what it holds) needs the right everywhere in it: nobody empties what
    /// they can't see. An unknown or unseen path is answered as invalid.
    /// </summary>
    private static bool MayRemove(AccessState state, LibraryPaths paths, string relative) =>
        state.Check(paths, relative, Rights.RemoveFromIndex) == PathVerdict.Allowed
        && state.Access.FolderPolicy.AllowsThroughout(paths.TryResolve(relative, out var full) ? paths.ToRelative(full) ?? relative : relative, Rights.RemoveFromIndex, state.Access.Rights);

    private static async Task<Results<Ok<IndexedFolder>, ValidationProblem>> GetFolder(
        DirectoryIndexer indexer, LibraryPaths paths, CurrentAccess current, Microsoft.Extensions.Options.IOptionsMonitor<Sfe.Core.Options.SfeOptions> options, string? path,
        CancellationToken cancellationToken)
    {
        var relative = LibraryPaths.NormalizeRelative(path);
        if (relative is null || !MayRemove(await current.GetAsync(), paths, relative))
            return InvalidPath();
        var stats = await indexer.GetStatsAsync(paths.Resolve(relative), cancellationToken);
        var excluded = options.CurrentValue.Scan.ExcludedFolders.Contains(relative, LibraryPaths.PathComparer);
        return TypedResults.Ok(new IndexedFolder(relative, stats.Indexed, stats.Directories, stats.Files, stats.TaggedFiles, excluded));
    }

    private static async Task<Results<Ok<RemovedFromIndex>, Conflict<ScanStatus>, ValidationProblem>> RemoveFolder(
        ScanCoordinator coordinator, SettingsService settings, LibraryPaths paths, CurrentAccess current, string? path, bool exclude = false,
        CancellationToken cancellationToken = default)
    {
        var relative = LibraryPaths.NormalizeRelative(path);
        if (relative is null || !MayRemove(await current.GetAsync(), paths, relative))
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

    private static async Task<Results<Accepted<ScanStatus>, Conflict<ScanStatus>, ValidationProblem>> Start(
        ScanCoordinator coordinator, LibraryPaths paths, CurrentAccess current, ScanRequest? request)
    {
        var path = LibraryPaths.NormalizeRelative(request?.Path);
        if (path is null || (await current.GetAsync()).Check(paths, path, Rights.Scan) != PathVerdict.Allowed)
            return TypedResults.ValidationProblem(new Dictionary<string, string[]> { ["path"] = ["Invalid library path."] });

        return coordinator.TryStart(path)
            ? TypedResults.Accepted("/api/scan", coordinator.Status)
            : TypedResults.Conflict(coordinator.Status);
    }
}
