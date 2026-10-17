using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Sfe.Core.Access;
using Sfe.Core.Data;
using Sfe.Core.Indexing;
using Sfe.Core.Library;
using Sfe.Core.Options;
using Sfe.Core.Paths;
using Sfe.Core.Search;
using Sfe.Core.Tagging;
using Sfe.Core.Tasks;
using Sfe.Core.Thumbnails;
using Sfe.Core.Trickplay;

namespace Sfe.Core.Files;

/// <summary>A file or folder in the trash, as the trash page lists it.</summary>
/// <param name="Path">Where it was, relative to the library root.</param>
/// <param name="Size">Bytes as the index knew them; null when it was not indexed.</param>
/// <param name="HasRules">A folder that had folder access rules (kept in the entry): its access isn't the parent's.</param>
public sealed record TrashItem(int Id, string Path, string Name, bool IsFolder, long? Size, int? Files, DateTimeOffset DeletedAt, string? DeletedBy, int? DeletedById,
    bool HasRules = false);

/// <param name="Next">The <c>before</c> of the next page; null on the last.</param>
/// <param name="RetentionDays">Days things stay in the trash (<see cref="TrashOptions.RetentionDays"/>); 0 = until emptied.</param>
public sealed record TrashPage(IReadOnlyList<TrashItem> Items, bool HasMore, int? Next, int RetentionDays);

public enum TrashPurgeState
{
    Idle,
    Running,
}

public sealed record TrashPurgeStatus(TrashPurgeState State, int Purged, DateTimeOffset? Started);

/// <summary>
/// The library's trash (agents/docs/features/trash.md): what is deleted goes to <c>trash/{guid}/{name}</c> in the work
/// folder (<see cref="WorkFolder"/>), with a <see cref="TrashEntry"/> keeping where it was, who deleted it, the tags of
/// its files and the folder rules of a folder, all given back by a restore. Purged after
/// <see cref="TrashOptions.RetentionDays"/> by a background task. The routes check rights and folder rules; the
/// callers hold the scan gate (<see cref="ScanCoordinator.TryBeginOperation"/>).
/// </summary>
public sealed class Trash(
    IDbContextFactory<SfeDbContext> dbFactory,
    LibraryPaths paths,
    WorkFolder work,
    DirectoryIndexer indexer,
    LibraryBrowser browser,
    TagService tags,
    ThumbnailService thumbnails,
    TrickplayService trickplay,
    ScanCoordinator scans,
    TaskHistory history,
    TenantBinding tenant,
    IOptionsMonitor<SfeOptions> options,
    ILogger<Trash> logger,
    FolderRuleService? rules = null) : IDisposable
{
    public const string FolderName = "trash";
    public const int MaxPageSize = 200;

    /// <summary>The purge runs by itself when the trash is used, at most this often.</summary>
    private static readonly TimeSpan AutomaticPurgeInterval = TimeSpan.FromHours(12);

    private readonly Lock _gate = new();
    private readonly CancellationTokenSource _shutdown = new();
    private CancellationTokenSource? _current;
    private TrashPurgeState _state = TrashPurgeState.Idle;
    private int _purged;
    private DateTimeOffset? _started;
    private DateTimeOffset? _lastAutomatic;

    private readonly Lock _cacheGate = new();
    private Task _cacheDeletes = Task.CompletedTask;

    /// <summary>The purge running; for tests.</summary>
    internal Task Completion { get; private set; } = Task.CompletedTask;

    /// <summary>Caches of deleted files still being deleted in the background; for tests.</summary>
    internal Task CacheDeletes
    {
        get
        {
            lock (_cacheGate)
                return _cacheDeletes;
        }
    }

    public int RetentionDays => Math.Max(0, options.CurrentValue.Files.Trash.RetentionDays);

    /// <summary>The space deletes everything for good (<see cref="FileManagementOptions.DeletePermanently"/>).</summary>
    public bool AlwaysPermanent => options.CurrentValue.Files.DeletePermanently;

    /* ---------- Deleting ---------- */

    /// <summary>
    /// Moves <paramref name="full"/> to the trash: its entry first (a crash then leaves an entry to purge, never a file
    /// lost), the disk, then the tags of its files and a folder's rules into the entry and its index rows out.
    /// </summary>
    /// <exception cref="IOException">The disk refused (in use, a folder with links on another drive...).</exception>
    /// <exception cref="UnauthorizedAccessException">The server's account may not change it.</exception>
    internal async Task<int> MoveInAsync(string full, string relative, bool isFolder, FileActor actor, CancellationToken cancellationToken)
    {
        var (size, files) = await MeasureAsync(full, isFolder, cancellationToken);
        var entry = new TrashEntry
        {
            Path = relative, Name = Path.GetFileName(full), IsFolder = isFolder, Size = size, Files = files, DeletedAt = DateTime.UtcNow,
            DeletedById = actor.AccountId, DeletedBy = actor.Name, Location = work.Location, Folder = Guid.NewGuid().ToString("N"),
        };
        await using (var db = await dbFactory.CreateDbContextAsync(cancellationToken))
        {
            db.TrashEntries.Add(entry);
            await db.SaveChangesAsync(cancellationToken);
        }

        var holder = Path.Combine(work.Get(FolderName), entry.Folder);
        try
        {
            Directory.CreateDirectory(holder);
            FileTree.Move(full, Path.Combine(holder, entry.Name), isFolder);
        }
        catch
        {
            await ForgetAsync(entry.Id, holder);
            throw;
        }

        // The disk has changed: what follows can't undo it.
        try
        {
            await KeepTagsAndForgetRowsAsync(entry.Id, full, CancellationToken.None);
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "The index rows of {Path} could not be moved to the trash: the next scan forgets them", relative);
        }
        if (isFolder && rules is not null && tenant.TenantId is { } tenantId)
        {
            try
            {
                var kept = await rules.TakeTreeAsync(tenantId, relative, CancellationToken.None);
                if (kept.Count > 0)
                {
                    await using var db = await dbFactory.CreateDbContextAsync();
                    await db.TrashEntries.Where(t => t.Id == entry.Id).ExecuteUpdateAsync(s => s.SetProperty(t => t.Rules, JsonSerializer.Serialize(kept)));
                }
            }
            catch (Exception ex)
            {
                logger.LogWarning(ex, "The folder rules of {Path} could not be kept with it in the trash", relative);
            }
        }

        Changed(full);
        logger.LogInformation("{Account} deleted {Path} to the trash", actor.Name, relative);
        PurgeWhenDue();
        return entry.Id;
    }

    /// <summary>Deletes <paramref name="full"/> for good: the disk, then its index rows, folder rules and cached previews.</summary>
    internal async Task DeleteForGoodAsync(string full, string relative, bool isFolder, FileActor actor, CancellationToken cancellationToken)
    {
        // The files whose previews go with them, read before their rows go.
        List<string> cached;
        await using (var db = await dbFactory.CreateDbContextAsync(cancellationToken))
            cached = isFolder
                ? await db.Files.Where(SearchPredicateBuilder.InTree<FileEntry>(f => f.FullName, full)).Select(f => f.FullName).ToListAsync(cancellationToken)
                : [full];

        FileTree.Delete(full);

        try
        {
            await using var db = await dbFactory.CreateDbContextAsync();
            await ForgetRowsAsync(db, full, CancellationToken.None);
            if (isFolder && rules is not null && tenant.TenantId is { } tenantId)
                await rules.TakeTreeAsync(tenantId, relative, CancellationToken.None);
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "What SFE kept about {Path} could not all be forgotten: the next scan does", relative);
        }
        DeleteCachesLater(cached);
        Changed(full);
        logger.LogInformation("{Account} deleted {Path} permanently", actor.Name, relative);
    }

    /* ---------- The trash page ---------- */

    /// <summary>Newest first, the entries <paramref name="visible"/> lets through.</summary>
    /// <param name="before">The <see cref="TrashPage.Next"/> of the previous page.</param>
    public async Task<TrashPage> ListAsync(int? before, int? limit, Func<TrashItem, bool> visible, CancellationToken cancellationToken = default)
    {
        PurgeWhenDue();
        var size = Math.Clamp(limit ?? 50, 1, MaxPageSize);
        var items = new List<TrashItem>();
        await using var db = await dbFactory.CreateDbContextAsync(cancellationToken);
        var cursor = before ?? int.MaxValue;
        var more = false;
        // What the person may see is decided per entry: read on until the page is full.
        while (items.Count <= size)
        {
            var batch = await db.TrashEntries.AsNoTracking().Where(t => t.Id < cursor).OrderByDescending(t => t.Id).Take(size * 2).ToListAsync(cancellationToken);
            if (batch.Count == 0)
                break;
            cursor = batch[^1].Id;
            items.AddRange(batch.Select(ToItem).Where(visible));
            if (batch.Count < size * 2)
                break;
        }
        if (items.Count > size)
        {
            more = true;
            items.RemoveRange(size, items.Count - size);
        }
        return new TrashPage(items, more, more ? items[^1].Id : null, RetentionDays);
    }

    public async Task<TrashItem?> FindAsync(int id, CancellationToken cancellationToken = default)
    {
        await using var db = await dbFactory.CreateDbContextAsync(cancellationToken);
        return await db.TrashEntries.AsNoTracking().Where(t => t.Id == id).Select(t => ToItem(t)).FirstOrDefaultAsync(cancellationToken);
    }

    /// <summary>
    /// Puts an entry back where it was (its folder made again if it is gone; " (2)" added when the name is taken there),
    /// with the tags of its files and a folder's rules; a folder is then scanned to index the rest.
    /// </summary>
    public async Task<FileOperationResult> RestoreAsync(int id, FileActor actor, CancellationToken cancellationToken = default)
    {
        await using var db = await dbFactory.CreateDbContextAsync(cancellationToken);
        var entry = await db.TrashEntries.Include(t => t.Tags).AsNoTracking().FirstOrDefaultAsync(t => t.Id == id, cancellationToken);
        if (entry is null)
            return FileOperationResult.Fail(FileOperationStatus.NotFound, "It is no longer in the trash.");
        var stored = StoragePath(entry);
        if (!File.Exists(stored) && !Directory.Exists(stored))
            return FileOperationResult.Fail(FileOperationStatus.NotFound, $"“{entry.Name}” is no longer in the trash's folder: it can't be restored.");

        var parent = ParentOf(entry.Path);
        if (!paths.TryResolve(parent, out var parentFull))
            return FileOperationResult.Fail(FileOperationStatus.Failed, $"The folder “{parent}” it was in can't be used any more.");
        string target;
        string targetPath;
        using (var operation = scans.TryBeginOperation(parentFull))
        {
            if (operation is null)
                return FileOperationResult.Fail(FileOperationStatus.ScanRunning, "A scan is running over the folder it was in: try again when it has finished.");
            try
            {
                Directory.CreateDirectory(parentFull);
                var name = File.Exists(Path.Combine(parentFull, entry.Name)) || Directory.Exists(Path.Combine(parentFull, entry.Name))
                    ? FileNames.KeepBoth(entry.Name, entry.IsFolder, n => File.Exists(Path.Combine(parentFull, n)) || Directory.Exists(Path.Combine(parentFull, n)))
                    : entry.Name;
                if (name is null)
                    return FileOperationResult.Fail(FileOperationStatus.Exists, $"There is already a file or folder named “{entry.Name}” there.");
                target = Path.Combine(parentFull, name);
                targetPath = parent.Length == 0 ? name : $"{parent}/{name}";
                FileTree.Move(stored, target, entry.IsFolder);
            }
            catch (LinksInsideException ex)
            {
                return FileOperationResult.Fail(FileOperationStatus.Failed, $"“{entry.Name}” can't be restored: {ex.Message}");
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                logger.LogWarning(ex, "{Account} could not restore {Path} from the trash", actor.Name, entry.Path);
                return FileOperations.DiskFailure(ex, $"“{entry.Name}” could not be restored");
            }

            // Back on the disk: what follows can't undo it.
            TryDeleteHolder(entry);
            if (entry.IsFolder && entry.Rules is { } json && rules is not null && tenant.TenantId is { } tenantId)
            {
                try
                {
                    await rules.PutBackAsync(tenantId, targetPath, JsonSerializer.Deserialize<List<KeptFolderRule>>(json) ?? [], CancellationToken.None);
                }
                catch (Exception ex)
                {
                    logger.LogWarning(ex, "The folder rules of {Path} could not be given back", targetPath);
                }
            }
            try
            {
                // A file is indexed at once (one file); a folder by the scan below.
                if (!entry.IsFolder)
                    await indexer.IndexFilesAsync([target], CancellationToken.None);
                await GiveTagsBackAsync(entry, target, CancellationToken.None);
            }
            catch (Exception ex)
            {
                logger.LogWarning(ex, "The tags of {Path} could not be given back", targetPath);
            }
            await using (var forget = await dbFactory.CreateDbContextAsync())
                await forget.TrashEntries.Where(t => t.Id == entry.Id).ExecuteDeleteAsync();
            Changed(target);
        }

        // The rest of a folder is indexed by a scan of it (none starts while another scan runs: the next one does).
        if (entry.IsFolder)
            scans.TryStart(targetPath, TaskTrigger.Automatic);
        logger.LogInformation("{Account} restored {Path} from the trash as {Target}", actor.Name, entry.Path, targetPath);
        return new FileOperationResult(FileOperationStatus.Done, browser.ToItem(entry.IsFolder ? new DirectoryInfo(target) : new FileInfo(target)));
    }

    /// <summary>Deletes an entry for good: its file or folder in the trash, the previews it had, its row.</summary>
    /// <returns>false when it was not in the trash any more.</returns>
    public async Task<bool> PurgeAsync(int id, FileActor? actor, CancellationToken cancellationToken = default)
    {
        TrashEntry? entry;
        await using (var db = await dbFactory.CreateDbContextAsync(cancellationToken))
            entry = await db.TrashEntries.AsNoTracking().FirstOrDefaultAsync(t => t.Id == id, cancellationToken);
        if (entry is null)
            return false;

        var stored = StoragePath(entry);
        // The previews were kept by the original paths.
        if (paths.TryResolve(entry.Path, out var original))
            DeleteCachesLater(entry.IsFolder
                ? [.. FileTree.Files(stored).Select(file => original + file[stored.Length..])]
                : [original]);
        if (File.Exists(stored) || Directory.Exists(stored))
            FileTree.Delete(stored);
        TryDeleteHolder(entry);
        await using (var db = await dbFactory.CreateDbContextAsync(cancellationToken))
            await db.TrashEntries.Where(t => t.Id == id).ExecuteDeleteAsync(cancellationToken);
        if (actor is not null)
            logger.LogInformation("{Account} deleted {Path} from the trash for good", actor.Name, entry.Path);
        return true;
    }

    /* ---------- The purge task ---------- */

    public TrashPurgeStatus PurgeStatus
    {
        get
        {
            lock (_gate)
                return new TrashPurgeStatus(_state, _purged, _started);
        }
    }

    /// <summary>Starts deleting for good what has been in the trash longer than <see cref="RetentionDays"/>.</summary>
    /// <returns>false when it runs already.</returns>
    public bool TryStartPurge(TaskTrigger trigger = TaskTrigger.Manual, int? scheduleId = null)
    {
        lock (_gate)
        {
            if (_state == TrashPurgeState.Running)
                return false;
            _state = TrashPurgeState.Running;
            _purged = 0;
            _started = DateTimeOffset.Now;
            _current?.Dispose();
            _current = CancellationTokenSource.CreateLinkedTokenSource(_shutdown.Token);
            var token = _current.Token;
            Completion = Task.Run(() => PurgeExpiredAsync(trigger, scheduleId, token), CancellationToken.None);
            return true;
        }
    }

    public bool CancelPurge()
    {
        lock (_gate)
        {
            if (_state != TrashPurgeState.Running)
                return false;
            _current?.Cancel();
            return true;
        }
    }

    private async Task PurgeExpiredAsync(TaskTrigger trigger, int? scheduleId, CancellationToken cancellationToken)
    {
        // From here on, what the purge logs goes into the run's log.
        var run = history.Begin(BackgroundTaskKind.TrashPurge, null, trigger, scheduleId);
        var outcome = TaskOutcome.Completed;
        string? summary;
        var days = RetentionDays;
        try
        {
            if (days == 0)
            {
                summary = "Nothing purged: the trash keeps everything until it is emptied.";
            }
            else
            {
                var limit = DateTime.UtcNow.AddDays(-days);
                List<int> expired;
                await using (var db = await dbFactory.CreateDbContextAsync(cancellationToken))
                    expired = await db.TrashEntries.Where(t => t.DeletedAt < limit).OrderBy(t => t.Id).Select(t => t.Id).ToListAsync(cancellationToken);
                var failed = 0;
                foreach (var id in expired)
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    try
                    {
                        if (await PurgeAsync(id, null, cancellationToken))
                            lock (_gate)
                                _purged++;
                    }
                    catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
                    {
                        failed++;
                        logger.LogWarning(ex, "Could not purge trash entry {Id}", id);
                    }
                }
                summary = $"{_purged:N0} deleted for good after {days} days in the trash" + (failed > 0 ? $", {failed:N0} could not be" : string.Empty);
                if (_purged > 0)
                    logger.LogInformation("Trash purge: {Summary}", summary);
            }
        }
        catch (OperationCanceledException)
        {
            outcome = TaskOutcome.Cancelled;
            summary = $"{_purged:N0} deleted for good before it was stopped";
        }
        catch (Exception ex)
        {
            outcome = TaskOutcome.Failed;
            summary = ex.Message;
            logger.LogError(ex, "Trash purge failed");
        }
        lock (_gate)
            _state = TrashPurgeState.Idle;
        await run.CompleteAsync(outcome, summary);
    }

    /// <summary>The purge starts by itself when the trash is used and it hasn't run for a while.</summary>
    private void PurgeWhenDue()
    {
        if (RetentionDays == 0)
            return;
        lock (_gate)
        {
            if (_lastAutomatic is { } last && DateTimeOffset.Now - last < AutomaticPurgeInterval)
                return;
            _lastAutomatic = DateTimeOffset.Now;
        }
        TryStartPurge(TaskTrigger.Automatic);
    }

    /* ---------- Parts ---------- */

    private string StoragePath(TrashEntry entry) =>
        Path.Combine(entry.Location == WorkFolderLocation.Server ? work.ServerPath : work.LibraryPath, FolderName, entry.Folder, entry.Name);

    private async Task<(long? Size, int? Files)> MeasureAsync(string full, bool isFolder, CancellationToken cancellationToken)
    {
        await using var db = await dbFactory.CreateDbContextAsync(cancellationToken);
        var indexed = db.Files.Where(SearchPredicateBuilder.InTree<FileEntry>(f => f.FullName, full));
        var files = await indexed.CountAsync(cancellationToken);
        if (!isFolder)
            return (new FileInfo(full).Length, null);
        if (files == 0 && !await db.Directories.AnyAsync(d => d.FullName == full, cancellationToken))
            return (null, null);
        return (await indexed.SumAsync(f => (long?)f.Size, cancellationToken) ?? 0, files);
    }

    /// <summary>The tags of the files at or below <paramref name="full"/> go into the entry; their index rows go away (not as missing files).</summary>
    private async Task KeepTagsAndForgetRowsAsync(int entryId, string full, CancellationToken cancellationToken)
    {
        await using var db = await dbFactory.CreateDbContextAsync(cancellationToken);
        var strategy = db.Database.CreateExecutionStrategy();
        await strategy.ExecuteAsync(async () =>
        {
            await using var transaction = await db.Database.BeginTransactionAsync(cancellationToken);
            // From the links (a join): SQLite can't run the APPLY a SelectMany over the files makes.
            var tagged = await db.FileTags.AsNoTracking()
                .Where(SearchPredicateBuilder.InTree<FileTag>(l => l.File!.FullName, full))
                .Select(l => new { l.File!.FullName, l.TagId })
                .ToListAsync(cancellationToken);
            foreach (var link in tagged)
            {
                db.TrashTags.Add(new TrashTag
                {
                    TrashEntryId = entryId,
                    Path = link.FullName.Length <= full.Length ? string.Empty : link.FullName[(full.Length + 1)..].Replace(Path.DirectorySeparatorChar, '/'),
                    TagId = link.TagId,
                });
            }
            await db.SaveChangesAsync(cancellationToken);
            await ForgetRowsAsync(db, full, cancellationToken);
            await transaction.CommitAsync(cancellationToken);
            db.ChangeTracker.Clear();
        });
    }

    private static async Task ForgetRowsAsync(SfeDbContext db, string full, CancellationToken cancellationToken)
    {
        // A folder's files go with it (cascade); a file alone by its own row.
        await db.Directories.Where(SearchPredicateBuilder.InTree<DirectoryEntry>(d => d.FullName, full)).ExecuteDeleteAsync(cancellationToken);
        await db.Files.Where(f => f.FullName == full).ExecuteDeleteAsync(cancellationToken);
    }

    /// <summary>The tagged files of a restored entry are indexed at once and get their tags back.</summary>
    private async Task GiveTagsBackAsync(TrashEntry entry, string target, CancellationToken cancellationToken)
    {
        if (entry.Tags.Count == 0)
            return;
        var byFile = entry.Tags
            .GroupBy(t => t.Path.Length == 0 ? target : Path.Combine(target, t.Path.Replace('/', Path.DirectorySeparatorChar)), LibraryPaths.PathComparer)
            .ToDictionary(g => g.Key, g => g.Select(t => t.TagId).Distinct().ToList(), LibraryPaths.PathComparer);
        var ids = await indexer.IndexFilesAsync([.. byFile.Keys], cancellationToken);
        await using var db = await dbFactory.CreateDbContextAsync(cancellationToken);
        var known = (await db.Tags.Select(t => t.Id).ToListAsync(cancellationToken)).ToHashSet();
        foreach (var (file, tagIds) in byFile)
        {
            if (!ids.TryGetValue(file, out var fileId))
                continue;
            var existing = await db.FileTags.Where(l => l.FileId == fileId).Select(l => l.TagId).ToListAsync(cancellationToken);
            foreach (var tagId in tagIds.Where(known.Contains).Except(existing))
                db.FileTags.Add(new FileTag { FileId = fileId, TagId = tagId });
        }
        await db.SaveChangesAsync(cancellationToken);
    }

    /// <summary>An entry whose disk step failed: nothing of it stays.</summary>
    private async Task ForgetAsync(int entryId, string holder)
    {
        try
        {
            await using var db = await dbFactory.CreateDbContextAsync();
            await db.TrashEntries.Where(t => t.Id == entryId).ExecuteDeleteAsync();
            if (Directory.Exists(holder) && !Directory.EnumerateFileSystemEntries(holder).Any())
                Directory.Delete(holder);
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Could not forget trash entry {Id}", entryId);
        }
    }

    private void TryDeleteHolder(TrashEntry entry)
    {
        try
        {
            var holder = Path.GetDirectoryName(StoragePath(entry))!;
            if (Directory.Exists(holder) && !Directory.EnumerateFileSystemEntries(holder).Any())
                Directory.Delete(holder);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            logger.LogDebug(ex, "Could not delete the trash folder of entry {Id}", entry.Id);
        }
    }

    private void Changed(string full)
    {
        if (Path.GetDirectoryName(full) is { } parent)
            browser.Invalidate(parent);
        tags.InvalidateFileCounts();
    }

    /// <summary>The thumbnails and trickplay of deleted files go, in the background (a folder can hold many).</summary>
    private void DeleteCachesLater(IReadOnlyList<string> files)
    {
        if (files.Count == 0)
            return;
        lock (_cacheGate)
            _cacheDeletes = _cacheDeletes.ContinueWith(_ =>
            {
                foreach (var file in files)
                {
                    try
                    {
                        File.Delete(thumbnails.GetCachePath(file));
                        var folder = trickplay.GetCacheFolder(file);
                        if (Directory.Exists(folder))
                            Directory.Delete(folder, recursive: true);
                    }
                    catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
                    {
                        logger.LogDebug(ex, "Could not delete the cached previews of {File}", file);
                    }
                }
            }, CancellationToken.None, TaskContinuationOptions.None, TaskScheduler.Default);
    }

    private static TrashItem ToItem(TrashEntry entry) => new(
        entry.Id, entry.Path, entry.Name, entry.IsFolder, entry.Size, entry.Files,
        new DateTimeOffset(DateTime.SpecifyKind(entry.DeletedAt, DateTimeKind.Utc)), entry.DeletedBy, entry.DeletedById, entry.Rules is not null);

    private static string ParentOf(string path) => path.LastIndexOf('/') is var slash and >= 0 ? path[..slash] : string.Empty;

    public void Dispose()
    {
        _shutdown.Cancel();
        _shutdown.Dispose();
        _current?.Dispose();
    }
}
