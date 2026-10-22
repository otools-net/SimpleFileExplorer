using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.Extensions.Options;
using Sfe.Api.Security;
using Sfe.Core.Access;
using Sfe.Core.Indexing;
using Sfe.Core.Library;
using Sfe.Core.Options;
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

/// <summary>Puts a folder on the lists that keep it out of scans, trickplay or thumbnails (true), or takes it off (false); null leaves a list alone.</summary>
/// <param name="Path">The folder, relative to the library root (not the root itself).</param>
public sealed record FolderExclusionChange(string? Path, bool? Scan, bool? Trickplay, bool? Thumbnails);

/// <summary>A folder on the lists that keep folders out of scans, trickplay or thumbnails, and which ones.</summary>
/// <param name="Path">The folder as listed, relative to the library root.</param>
/// <param name="Exists">The folder is on the disk (a folder renamed or deleted outside SFE stays listed until removed).</param>
public sealed record ExcludedFolder(string Path, bool Scan, bool Trickplay, bool Thumbnails, bool Exists);

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
            .WithSummary("Starts indexing a folder recursively in the background.")
            .WithDescription("""
                Folders excluded from scans (`Scan:ExcludedFolders`) are skipped and what the index has of them stays
                as it is; a folder excluded itself, or inside an excluded one, is refused (400).
                """);

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

        index.MapGet("/exclusions", ListExclusions)
            .RequireRight(Rights.ExcludeFolders)
            .FolderScoped("only the folders the subject sees")
            .WithName("GetFolderExclusions")
            .WithSummary("The folders kept out of scans, trickplay or thumbnails in the space, with which of them.");

        index.MapPut("/exclusions", SetExclusions)
            .RequireRight(Rights.ExcludeFolders)
            .FolderScoped("index.exclude at the folder")
            .WithName("SetFolderExclusions")
            .WithSummary("Keeps a folder (with its sub-folders) out of scans, trickplay or thumbnails, or lets them in again.")
            .WithDescription("""
                Changes the space's `Scan:ExcludedFolders`, `Trickplay:ExcludedFolders` and `Thumbnails:ExcludedFolders`.
                A folder no longer on the disk can only be taken off them.
                What is already indexed or made stays: scans keep the folder's index rows as they are. A folder excluded
                through a listed parent can only be let in again by taking the parent off the list (400). Answers what
                the folder is kept out of afterwards.
                """);

        return api;
    }

    private static async Task<Ok<IReadOnlyList<ExcludedFolder>>> ListExclusions(LibraryPaths paths, CurrentAccess current, IOptionsMonitor<SfeOptions> options)
    {
        var state = await current.GetAsync();
        var exclusions = FolderExclusions.Of(options.CurrentValue);
        IReadOnlyList<ExcludedFolder> folders = [.. exclusions.Folders()
            .Where(folder => state.Check(paths, folder, Rights.ExcludeFolders) != PathVerdict.NotFound)
            .Select(folder => new ExcludedFolder(folder,
                exclusions.Lists(ExclusionKind.Scan, folder),
                exclusions.Lists(ExclusionKind.Trickplay, folder),
                exclusions.Lists(ExclusionKind.Thumbnails, folder),
                paths.TryResolve(folder, out var full) && Directory.Exists(full)))];
        return TypedResults.Ok(folders);
    }

    private static async Task<Results<Ok<FolderExclusionState>, ValidationProblem, NotFound, ForbidHttpResult>> SetExclusions(
        SettingsService settings, LibraryPaths paths, CurrentAccess current, IOptionsMonitor<SfeOptions> options, ILoggerFactory loggerFactory,
        FolderExclusionChange change, CancellationToken cancellationToken)
    {
        var relative = LibraryPaths.NormalizeRelative(change.Path);
        if (relative is null)
            return InvalidPath();
        var verdict = (await current.GetAsync()).Check(paths, relative, Rights.ExcludeFolders);
        if (verdict == PathVerdict.Forbidden)
            return TypedResults.Forbid();
        if (verdict != PathVerdict.Allowed || !paths.TryResolve(relative, out var full) || paths.ToRelative(full) is not { } folder)
            return TypedResults.NotFound();
        // A folder gone from the disk can still be taken off the lists, not put on them.
        var removing = new[] { change.Scan, change.Trickplay, change.Thumbnails }.All(v => v is null or false);
        if (!Directory.Exists(full)
            && (!removing || File.Exists(full) || !FolderExclusions.Of(options.CurrentValue).Folders().Contains(folder, LibraryPaths.PathComparer)))
            return TypedResults.NotFound();
        if (folder.Length == 0)
            return Problem("path", "The library itself can't be excluded: exclude some of its folders.");

        var asked = new Dictionary<ExclusionKind, bool>();
        foreach (var (kind, value) in new[] { (ExclusionKind.Scan, change.Scan), (ExclusionKind.Trickplay, change.Trickplay), (ExclusionKind.Thumbnails, change.Thumbnails) })
            if (value is { } excluded)
                asked[kind] = excluded;

        var before = FolderExclusions.Of(options.CurrentValue);
        foreach (var (kind, excluded) in asked)
            if (!excluded && before.Through(kind, folder) is { } parent && !LibraryPaths.PathComparer.Equals(parent, folder))
                return Problem(Key(kind), $"Excluded through “{parent}”: take that folder off the list to let this one in again.");

        var keys = await settings.SetExclusionsAsync(folder, asked, cancellationToken: cancellationToken);
        var after = FolderExclusions.Of(options.CurrentValue).At(folder);
        // A list set by the server's configuration (Sfe:Locked) can't be changed from here.
        foreach (var (kind, excluded) in asked)
            if ((after.Of(kind) is not null) != excluded)
                return Problem(Key(kind), "This list is set by the server's configuration: it can't be changed here.");

        if (keys.Count > 0)
            loggerFactory.CreateLogger(typeof(ScanEndpoints)).LogInformation("Folder exclusions of {Folder} changed: {Changes}",
                folder, string.Join(", ", asked.Select(a => $"{a.Key} {(a.Value ? "excluded" : "included")}")));
        return TypedResults.Ok(after);

        static string Key(ExclusionKind kind) => kind.ToString().ToLowerInvariant();
    }

    private static ValidationProblem Problem(string key, string message) =>
        TypedResults.ValidationProblem(new Dictionary<string, string[]> { [key] = [message] });

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
        ScanCoordinator coordinator, LibraryPaths paths, CurrentAccess current, Microsoft.Extensions.Options.IOptionsMonitor<Sfe.Core.Options.SfeOptions> options,
        ScanRequest? request)
    {
        var path = LibraryPaths.NormalizeRelative(request?.Path);
        if (path is null || (await current.GetAsync()).Check(paths, path, Rights.Scan) != PathVerdict.Allowed)
            return TypedResults.ValidationProblem(new Dictionary<string, string[]> { ["path"] = ["Invalid library path."] });
        // Nothing to do there: the scan would leave it as it is.
        if (paths.TryResolve(path, out var full) && paths.ToRelative(full) is { } canonical
            && FolderExclusions.Of(options.CurrentValue).Through(ExclusionKind.Scan, canonical) is { } through)
            return TypedResults.ValidationProblem(new Dictionary<string, string[]>
            {
                ["path"] = [LibraryPaths.PathComparer.Equals(through, canonical)
                    ? $"“{through}” is excluded from scans: what the index has of it stays as it is."
                    : $"This folder is inside “{through}”, which is excluded from scans: what the index has of it stays as it is."],
            });

        return coordinator.TryStart(path)
            ? TypedResults.Accepted("/api/scan", coordinator.Status)
            : TypedResults.Conflict(coordinator.Status);
    }
}
