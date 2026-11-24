using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Otools.Net.Sfe.Core.Data;
using Otools.Net.Sfe.Core.Indexing;
using Otools.Net.Sfe.Core.Library;
using Otools.Net.Sfe.Core.Paths;

namespace Otools.Net.Sfe.Core.Files;

/// <summary>
/// The previous versions of a library file (agents/docs/features/previous-versions.md): the copies of its content the
/// trash keeps before it is saved over (<see cref="TrashEntryKind.PreviousVersion"/>), listed, opened, restored as a
/// copy next to it, or restored over it (what is there then becomes a version in turn). The routes check rights and
/// folder rules.
/// </summary>
public sealed class PreviousVersions(
    IDbContextFactory<SfeDbContext> dbFactory,
    Trash trash,
    ScanCoordinator scans,
    DirectoryIndexer indexer,
    LibraryBrowser browser,
    ILogger<PreviousVersions> logger
)
{
    /// <summary>The most versions listed for a file (newest first): the trash's retention keeps them few.</summary>
    public const int MaxListed = 100;

    /// <summary>
    /// The versions of the file <paramref name="canonical"/> (as <see cref="LibraryPaths.ToRelative"/> gives
    /// it), newest first. The trash holds few versions (they are purged): all are read and their paths compared as
    /// the file system does (the databases don't compare case alike).
    /// </summary>
    public async Task<IReadOnlyList<TrashItem>> ListAsync(
        string canonical,
        CancellationToken cancellationToken = default
    )
    {
        await using var db = await dbFactory.CreateDbContextAsync(cancellationToken);
        var entries = await db
            .TrashEntries.AsNoTracking()
            .Where(t => t.Kind == TrashEntryKind.PreviousVersion)
            .ToListAsync(cancellationToken);
        return entries
            .Where(t => LibraryPaths.PathComparer.Equals(t.Path, canonical))
            .OrderByDescending(t => t.DeletedAt)
            .ThenByDescending(t => t.Id)
            .Take(MaxListed)
            .Select(Trash.ToItem)
            .ToList();
    }

    /// <summary>Where the content of the version <paramref name="id"/> is kept; null when it is not there.</summary>
    public async Task<string?> ContentPathAsync(int id, CancellationToken cancellationToken = default)
    {
        var entry = await FindAsync(id, cancellationToken);
        return entry is not null && trash.StoragePath(entry) is var stored && File.Exists(stored) ? stored : null;
    }

    /// <summary>
    /// Copies the version <paramref name="id"/> next to the file <paramref name="full"/> (<paramref name="canonical"/>
    /// in the library; " (2)" added to the name): the version stays where it is.
    /// </summary>
    public async Task<FileOperationResult> RestoreAsCopyAsync(
        int id,
        string full,
        string canonical,
        FileActor actor,
        CancellationToken cancellationToken = default
    )
    {
        if (await ContentPathAsync(id, cancellationToken) is not { } stored)
            return Gone();
        var folder = Path.GetDirectoryName(full)!;
        if (!Directory.Exists(folder))
            return FileOperationResult.Fail(FileOperationStatus.NotFound, "The file's folder is not there any more.");
        using var operation = scans.TryBeginOperation(folder);
        if (operation is null)
            return ScanRunning();

        var name = FileNames.KeepBoth(
            Path.GetFileName(full),
            isFolder: false,
            n => File.Exists(Path.Combine(folder, n)) || Directory.Exists(Path.Combine(folder, n))
        );
        if (name is null)
            return FileOperationResult.Fail(FileOperationStatus.Exists, "No free name was found for the copy.");
        var target = Path.Combine(folder, name);
        try
        {
            File.Copy(stored, target);
            File.SetLastWriteTimeUtc(target, DateTime.UtcNow);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            logger.LogWarning(ex, "{Account} could not restore a previous version of {Path}", actor.Name, canonical);
            return FileOperations.DiskFailure(ex, "The previous version could not be copied");
        }

        await IndexAsync(target, () => indexer.IndexFilesAsync([target], CancellationToken.None));
        logger.LogInformation(
            "{Account} restored a previous version of {Path} as {Name}",
            actor.Name,
            canonical,
            name
        );
        return new FileOperationResult(FileOperationStatus.Done, browser.ToItem(new FileInfo(target)));
    }

    /// <summary>
    /// Puts the version <paramref name="id"/> in place of the file <paramref name="full"/>
    /// (<paramref name="canonical"/> in the library), swapped in whole: the content there first becomes a version
    /// itself, the one restored leaves the list. The file keeps its tags and its place in the index.
    /// </summary>
    public async Task<FileOperationResult> RestoreOverAsync(
        int id,
        string full,
        string canonical,
        FileActor actor,
        CancellationToken cancellationToken = default
    )
    {
        if (await ContentPathAsync(id, cancellationToken) is not { } stored)
            return Gone();
        if (new FileInfo(full) is var info && !info.Exists)
            return FileOperationResult.Fail(FileOperationStatus.NotFound, "The file is not there any more.");
        if (info.LinkTarget is not null || info.IsReadOnly)
        {
            return FileOperationResult.Fail(
                FileOperationStatus.Forbidden,
                "This file is a link or is marked read-only on the server: restore the version as a copy instead."
            );
        }
        using var operation = scans.TryBeginOperation(full);
        if (operation is null)
            return ScanRunning();

        int keptId;
        try
        {
            keptId = await trash.CopyInAsync(full, canonical, actor, cancellationToken);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            logger.LogWarning(ex, "{Account} could not keep {Path} before restoring a version", actor.Name, canonical);
            return FileOperations.DiskFailure(ex, "The file as it is now could not be kept in the trash");
        }
        if (await SwapInAsync(stored, full) is { } failure)
        {
            await trash.PurgeAsync(keptId, null, CancellationToken.None);
            logger.LogWarning(failure, "{Account} could not restore a version of {Path}", actor.Name, canonical);
            return FileOperations.DiskFailure(failure, "The previous version could not be restored");
        }

        // Restored: what follows can't undo it.
        await trash.PurgeAsync(id, null, CancellationToken.None);
        await IndexAsync(full, () => indexer.RefreshFileAsync(full, CancellationToken.None));
        logger.LogInformation("{Account} restored a previous version of {Path}", actor.Name, canonical);
        return new FileOperationResult(FileOperationStatus.Done, browser.ToItem(new FileInfo(full)), TrashId: keptId);
    }

    /// <summary>The version's content copied beside the file, then swapped in whole (never half a file).</summary>
    /// <returns>What the disk refused; null when done.</returns>
    private static async Task<Exception?> SwapInAsync(string stored, string full)
    {
        var temporary = Path.Combine(
            Path.GetDirectoryName(full)!,
            $".{Path.GetFileName(full)}.{Guid.NewGuid():N}.sfe-tmp"
        );
        try
        {
            File.Copy(stored, temporary);
            // A new date: previews, editors and listings see a change.
            File.SetLastWriteTimeUtc(temporary, DateTime.UtcNow);
            if (!OperatingSystem.IsWindows())
                File.SetUnixFileMode(temporary, File.GetUnixFileMode(full));
            await FileContent.ReplaceAsync(temporary, full);
            return null;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            try
            {
                File.Delete(temporary);
            }
            catch (Exception cleanup) when (cleanup is IOException or UnauthorizedAccessException)
            {
                // Left over as a hidden file, as a save cut off leaves one.
            }
            return ex;
        }
    }

    /// <summary>The file indexed by <paramref name="index"/>, then the listings of its folder refreshed.</summary>
    private async Task IndexAsync(string full, Func<Task> index)
    {
        try
        {
            await index();
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "The index could not take {Path}: the next scan does", full);
        }
        browser.Invalidate(Path.GetDirectoryName(full)!);
    }

    private async Task<TrashEntry?> FindAsync(int id, CancellationToken cancellationToken)
    {
        await using var db = await dbFactory.CreateDbContextAsync(cancellationToken);
        return await db
            .TrashEntries.AsNoTracking()
            .FirstOrDefaultAsync(t => t.Id == id && t.Kind == TrashEntryKind.PreviousVersion, cancellationToken);
    }

    private static FileOperationResult Gone() =>
        FileOperationResult.Fail(FileOperationStatus.NotFound, "This version is no longer in the trash.");

    private static FileOperationResult ScanRunning() =>
        FileOperationResult.Fail(
            FileOperationStatus.ScanRunning,
            "A scan is running over this folder: try again when it has finished."
        );
}
