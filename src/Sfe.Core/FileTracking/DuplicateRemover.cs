using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Sfe.Core.Data;
using Sfe.Core.Files;
using Sfe.Core.Indexing;
using Sfe.Core.Options;
using Sfe.Core.Paths;
using Sfe.Core.Tagging;

namespace Sfe.Core.FileTracking;

/// <summary>
/// Removes one copy of a duplicate, after adding its tags to the copy kept: to the library's <see cref="Trash"/>, or for
/// good when the space deletes permanently (<see cref="FileManagementOptions.DeletePermanently"/>). The content must
/// never be lost: the copies are compared byte for byte (the fingerprint only covers the first megabyte), and two paths
/// leading to the same file (folder links) are refused.
/// </summary>
public sealed class DuplicateRemover(
    IDbContextFactory<SfeDbContext> dbFactory,
    LibraryPaths paths,
    DirectoryIndexer indexer,
    TagService tags,
    Trash trash,
    ScanCoordinator scans,
    IOptionsMonitor<SfeOptions> options,
    ILogger<DuplicateRemover> logger)
{
    public bool IsEnabled => options.CurrentValue.FileTracking.Duplicates.AllowRemove;

    /// <param name="relativePath">The copy to delete.</param>
    /// <param name="relativeKeep">An identical copy, kept: it gets the tags of the deleted one.</param>
    /// <param name="actor">Who removes it: the trash entry is theirs.</param>
    public async Task<DuplicateRemoval> RemoveAsync(string? relativePath, string? relativeKeep, FileActor actor, CancellationToken cancellationToken = default)
    {
        if (!IsEnabled)
            return DuplicateRemoval.Fail(DuplicateRemovalStatus.Disabled, "Removing duplicates is turned off (Administration › Duplicates & moves › Settings).");
        if (!paths.TryResolve(relativePath, out var remove) || !File.Exists(remove))
            return DuplicateRemoval.Fail(DuplicateRemovalStatus.Invalid, $"“{relativePath}” is not a file of the library.", "path");
        if (!paths.TryResolve(relativeKeep, out var keep) || !File.Exists(keep))
            return DuplicateRemoval.Fail(DuplicateRemovalStatus.Invalid, $"“{relativeKeep}” is not a file of the library.", "keep");

        try
        {
            if (LibraryPaths.PathComparer.Equals(remove, keep) || FileIdentity.SameFile(remove, keep))
                return DuplicateRemoval.Fail(DuplicateRemovalStatus.Invalid, "Both paths lead to the same file (through a folder link): deleting one would delete the only copy.", "keep");
            if (!await FileIdentity.SameContentAsync(remove, keep, cancellationToken))
                return DuplicateRemoval.Fail(DuplicateRemovalStatus.Invalid, "The files are not identical: their content differs past the part the fingerprint covers. Nothing was deleted.", "keep");
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return DuplicateRemoval.Fail(DuplicateRemovalStatus.Failed, $"The files could not be compared: {ex.Message}");
        }

        using var operation = scans.TryBeginOperation(remove);
        if (operation is null)
            return DuplicateRemoval.Fail(DuplicateRemovalStatus.Failed, "A scan is running over its folder: try again when it has finished.");

        var ids = await indexer.IndexFilesAsync([remove, keep], cancellationToken);
        if (!ids.TryGetValue(keep, out var keepId))
            return DuplicateRemoval.Fail(DuplicateRemovalStatus.Invalid, $"“{relativeKeep}” cannot be indexed (ignored, or its path is too long), so it cannot take the tags.", "keep");

        // Tags first: if the delete then fails, both copies have them, which loses nothing.
        if (ids.TryGetValue(remove, out var removeId))
        {
            await using var db = await dbFactory.CreateDbContextAsync(cancellationToken);
            var moved = await db.FileTags.Where(l => l.FileId == removeId).Select(l => l.TagId).ToListAsync(cancellationToken);
            var existing = await db.FileTags.Where(l => l.FileId == keepId).Select(l => l.TagId).ToListAsync(cancellationToken);
            foreach (var tagId in moved.Except(existing))
                db.FileTags.Add(new FileTag { FileId = keepId, TagId = tagId });
            await db.SaveChangesAsync(cancellationToken);
        }

        // The trash then takes its index row (not kept as a missing file: its tags are on the copy kept) and keeps its
        // tags with it for a restore.
        var relative = paths.ToRelative(remove)!;     // resolved in the library above
        int? trashId = null;
        try
        {
            if (trash.AlwaysPermanent)
                await trash.DeleteForGoodAsync(remove, relative, isFolder: false, actor, cancellationToken);
            else
                trashId = await trash.MoveInAsync(remove, relative, isFolder: false, actor, cancellationToken);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            tags.InvalidateFileCounts();
            return DuplicateRemoval.Fail(DuplicateRemovalStatus.Failed, $"“{relativePath}” could not be {(trash.AlwaysPermanent ? "deleted" : "moved to the trash")}: {ex.Message}");
        }
        logger.LogInformation("Removed duplicate {Removed} ({Where}), kept {Kept}", remove, trashId is null ? "deleted" : "to the trash", keep);

        var kept = await tags.GetFileTagsAsync(paths.ToRelative(keep), cancellationToken);
        return new DuplicateRemoval(DuplicateRemovalStatus.Removed, Kept: kept, TrashId: trashId);
    }
}

public enum DuplicateRemovalStatus
{
    Removed,
    Disabled,
    Invalid,
    Failed,
}

/// <param name="Field">Request field an <see cref="DuplicateRemovalStatus.Invalid"/> error is about.</param>
/// <param name="Kept">Tags of the copy kept, now including those of the deleted one.</param>
/// <param name="TrashId">The trash entry of the copy removed, for an Undo; null when it was deleted for good.</param>
public sealed record DuplicateRemoval(DuplicateRemovalStatus Status, string? Error = null, string? Field = null, FileTags? Kept = null, int? TrashId = null)
{
    public static DuplicateRemoval Fail(DuplicateRemovalStatus status, string error, string? field = null) => new(status, error, field);
}
