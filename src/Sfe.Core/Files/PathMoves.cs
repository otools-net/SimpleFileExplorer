using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Sfe.Core.Access;
using Sfe.Core.Data;
using Sfe.Core.Indexing;
using Sfe.Core.Library;
using Sfe.Core.Paths;
using Sfe.Core.Search;
using Sfe.Core.Settings;
using Sfe.Core.Tagging;
using Sfe.Core.Thumbnails;
using Sfe.Core.Trickplay;

namespace Sfe.Core.Files;

/// <summary>
/// The one place where what SFE keeps by path follows a file or folder renamed or moved on the disk
/// (agents/docs/features/path-moves.md): the index rows (so tags stay on their files), the library's settings naming
/// folders, task schedules, quick links, the thumbnail and trickplay caches (with the video fingerprints), the listings
/// kept, the tag counts. Folder rules are moved around the disk step by <see cref="FolderRuleService.BeginMoveAsync"/>.
/// </summary>
/// <remarks>
/// Called once the disk has changed: nothing here can undo it, so each part is done on its own, and one that fails is
/// logged and left for the next scan (index rows of the old place become missing files, then relink) or for lazy
/// rebuilding (caches).
/// </remarks>
public sealed class PathMoves(
    IDbContextFactory<SfeDbContext> dbFactory,
    LibraryPaths paths,
    DirectoryIndexer indexer,
    LibraryBrowser browser,
    TagService tags,
    ThumbnailService thumbnails,
    TrickplayService trickplay,
    SettingsService settings,
    TenantBinding tenant,
    ILogger<PathMoves> logger,
    QuickLinkService? quickLinks = null)
{
    /// <summary>Index rows read per round trip when the caches of a folder's files move.</summary>
    private const int CacheBatchSize = 1000;

    /// <summary>Longer than any path: what <c>SUBSTRING</c> keeps of the end of a path (the rest of it).</summary>
    private const int PathRest = 32767;

    private readonly Lock _cacheGate = new();
    private Task _cacheMoves = Task.CompletedTask;

    /// <summary>The caches of moved folders still moving, in the background (for tests).</summary>
    internal Task CacheMoves
    {
        get
        {
            lock (_cacheGate)
                return _cacheMoves;
        }
    }

    /// <summary>
    /// What SFE keeps of <paramref name="from"/> (and below it, for a folder) follows it to <paramref name="to"/>
    /// (full paths inside the library). The disk has changed already.
    /// </summary>
    public async Task MoveAsync(string from, string to, bool isFolder, CancellationToken cancellationToken = default)
    {
        from = Path.TrimEndingDirectorySeparator(Path.GetFullPath(from));
        to = Path.TrimEndingDirectorySeparator(Path.GetFullPath(to));
        var fromRelative = paths.ToRelative(from) ?? throw new ArgumentException("Not a path of the library.", nameof(from));
        var toRelative = paths.ToRelative(to) ?? throw new ArgumentException("Not a path of the library.", nameof(to));

        await Step("index rows", () => MoveIndexAsync(from, to, isFolder, cancellationToken));
        if (isFolder)
        {
            await Step("settings", async () =>
            {
                if ((await settings.MoveFolderPathsAsync(fromRelative, toRelative, cancellationToken: cancellationToken)) is { Count: > 0 } keys)
                    logger.LogInformation("Settings {Keys} follow {From} to {To}", string.Join(", ", keys), fromRelative, toRelative);
            });
            await Step("task schedules", () => MoveSchedulesAsync(fromRelative, toRelative, cancellationToken));
        }
        if (quickLinks is not null && tenant.TenantId is { } tenantId)
            await Step("quick links", () => quickLinks.MovePathsAsync(tenantId, fromRelative, toRelative, cancellationToken));

        tags.InvalidateFileCounts();
        foreach (var folder in new[] { Path.GetDirectoryName(from), Path.GetDirectoryName(to), from, to }.OfType<string>())
            browser.Invalidate(folder);

        if (isFolder)
            QueueCacheMoves(from, to);
        else
            MoveCache(from, to);

        async Task Step(string what, Func<Task> step)
        {
            try
            {
                await step();
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                logger.LogWarning(ex, "The {What} of {From} could not follow it to {To}", what, fromRelative, toRelative);
            }
        }
    }

    /// <summary>
    /// The index rows of <paramref name="from"/> (and below it) take the new path, in one transaction, keeping their
    /// ids: tags, fingerprints and resolutions stay. Rows left at <paramref name="to"/> by something no longer there
    /// are forgotten first (tagged files kept as missing).
    /// </summary>
    private async Task MoveIndexAsync(string from, string to, bool isFolder, CancellationToken cancellationToken)
    {
        // A case-only rename on Windows: the same place for the file system (and the index's collation).
        if (!LibraryPaths.PathComparer.Equals(from, to))
            await indexer.ForgetAsync(to, cancellationToken);

        await using var db = await dbFactory.CreateDbContextAsync(cancellationToken);
        var strategy = db.Database.CreateExecutionStrategy();
        await strategy.ExecuteAsync(async () =>
        {
            await using var transaction = await db.Database.BeginTransactionAsync(cancellationToken);
            var name = Path.GetFileName(to);
            var parent = Path.GetDirectoryName(to);
            var cut = from.Length;
            if (isFolder)
            {
                var parentId = parent is null ? null : await indexer.FolderIdAsync(db, parent, create: false, cancellationToken);
                await db.Directories.Where(SearchPredicateBuilder.InTree<DirectoryEntry>(d => d.FullName, from))
                    .ExecuteUpdateAsync(s => s.SetProperty(d => d.FullName, d => to + d.FullName.Substring(cut, PathRest)), cancellationToken);
                await db.Directories.Where(d => d.FullName == to)
                    .ExecuteUpdateAsync(s => s.SetProperty(d => d.Name, name).SetProperty(d => d.ParentId, parentId), cancellationToken);
                await db.Files.Where(SearchPredicateBuilder.InTree<FileEntry>(f => f.FullName, from))
                    .ExecuteUpdateAsync(s => s.SetProperty(f => f.FullName, f => to + f.FullName.Substring(cut, PathRest)), cancellationToken);
            }
            else
            {
                var file = db.Files.Where(f => f.FullName == from);
                // A file's row needs its folder's: made when the folder has none yet (a file moved into a folder never scanned).
                if (parent is not null && await file.AnyAsync(cancellationToken)
                    && await indexer.FolderIdAsync(db, parent, create: true, cancellationToken) is { } folderId)
                {
                    var stem = Path.GetFileNameWithoutExtension(name);
                    var extension = Path.GetExtension(name);
                    await file.ExecuteUpdateAsync(s => s
                        .SetProperty(f => f.FullName, to)
                        .SetProperty(f => f.Name, stem)
                        .SetProperty(f => f.Extension, extension)
                        .SetProperty(f => f.DirectoryId, folderId), cancellationToken);
                }
                else
                {
                    // Its folder can't be indexed: the file isn't either, as after a scan.
                    await file.ExecuteDeleteAsync(cancellationToken);
                }
            }
            await transaction.CommitAsync(cancellationToken);
        });
    }

    /// <summary>Schedules scanning the folder (or one below it) scan it at its new place.</summary>
    private async Task MoveSchedulesAsync(string from, string to, CancellationToken cancellationToken)
    {
        await using var db = await dbFactory.CreateDbContextAsync(cancellationToken);
        // A few schedules per library: read them all and compare as the file system does.
        var schedules = await db.TaskSchedules.Where(s => s.Path != null && s.Path != "").ToListAsync(cancellationToken);
        foreach (var schedule in schedules)
        {
            if (LibraryPaths.NormalizeRelative(schedule.Path) is { Length: > 0 } path && FolderPolicy.IsSameOrBelow(path, from))
                schedule.Path = to + path[from.Length..];
        }
        if (await db.SaveChangesAsync(cancellationToken) is var changed and > 0)
            logger.LogInformation("{Count} task schedules follow {From} to {To}", changed, from, to);
    }

    private void QueueCacheMoves(string from, string to)
    {
        lock (_cacheGate)
            _cacheMoves = _cacheMoves.ContinueWith(_ => MoveCachesAsync(from, to), CancellationToken.None, TaskContinuationOptions.None, TaskScheduler.Default).Unwrap();
    }

    /// <summary>
    /// The caches of the files of a moved folder follow them, page by page of its index rows (already at their new
    /// place): in the background, as a folder can hold a hundred thousand files. Files not indexed lose theirs, which
    /// are made again when needed.
    /// </summary>
    private async Task MoveCachesAsync(string from, string to)
    {
        try
        {
            var lastId = 0;
            while (true)
            {
                await using var db = await dbFactory.CreateDbContextAsync();
                var batch = await db.Files.AsNoTracking()
                    .Where(SearchPredicateBuilder.InTree<FileEntry>(f => f.FullName, to))
                    .Where(f => f.Id > lastId)
                    .OrderBy(f => f.Id)
                    .Select(f => new { f.Id, f.FullName })
                    .Take(CacheBatchSize)
                    .ToListAsync();
                if (batch.Count == 0)
                    break;
                lastId = batch[^1].Id;
                foreach (var file in batch)
                {
                    if (file.FullName.Length >= to.Length)
                        MoveCache(from + file.FullName[to.Length..], file.FullName);
                }
            }
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "The cached previews of the files of {Folder} could not all follow it", to);
        }
    }

    /// <summary>
    /// The thumbnail and trickplay (with the video's fingerprint) of <paramref name="oldPath"/> become those of
    /// <paramref name="newPath"/>: both are keyed by path and checked against the file's modification time, which a
    /// rename or move keeps. Best effort.
    /// </summary>
    internal void MoveCache(string oldPath, string newPath)
    {
        if (string.Equals(oldPath, newPath, StringComparison.Ordinal))
            return;
        try
        {
            var oldThumbnail = thumbnails.GetCachePath(oldPath);
            var newThumbnail = thumbnails.GetCachePath(newPath);
            if (oldThumbnail != newThumbnail && File.Exists(oldThumbnail))
            {
                Directory.CreateDirectory(Path.GetDirectoryName(newThumbnail)!);
                File.Move(oldThumbnail, newThumbnail, overwrite: true);
            }

            var oldTrickplay = trickplay.GetCacheFolder(oldPath);
            var newTrickplay = trickplay.GetCacheFolder(newPath);
            if (oldTrickplay != newTrickplay && Directory.Exists(oldTrickplay))
            {
                if (Directory.Exists(newTrickplay))
                    Directory.Delete(newTrickplay, recursive: true);
                Directory.CreateDirectory(Path.GetDirectoryName(newTrickplay)!);
                Directory.Move(oldTrickplay, newTrickplay);
            }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            logger.LogDebug(ex, "Could not move the cached previews of {Old} to {New}", oldPath, newPath);
        }
    }
}
