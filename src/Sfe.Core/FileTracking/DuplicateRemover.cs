using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Sfe.Core.Data;
using Sfe.Core.Indexing;
using Sfe.Core.Options;
using Sfe.Core.Paths;
using Sfe.Core.Tagging;
using Sfe.Core.Thumbnails;
using Sfe.Core.Trickplay;

namespace Sfe.Core.FileTracking;

/// <summary>
/// Deletes one copy of a duplicate from the disk, after adding its tags to the copy kept. The client has no login, so
/// the content must never be lost: the copies are compared byte for byte (the fingerprint only covers the first
/// megabyte), and two paths leading to the same file (folder links) are refused.
/// </summary>
public sealed class DuplicateRemover(
    IDbContextFactory<SfeDbContext> dbFactory,
    LibraryPaths paths,
    DirectoryIndexer indexer,
    TagService tags,
    ThumbnailService thumbnails,
    TrickplayService trickplay,
    IOptionsMonitor<SfeOptions> options,
    ILogger<DuplicateRemover> logger)
{
    public bool IsEnabled => options.CurrentValue.FileTracking.Duplicates.AllowRemove;

    /// <param name="relativePath">The copy to delete.</param>
    /// <param name="relativeKeep">An identical copy, kept: it gets the tags of the deleted one.</param>
    public async Task<DuplicateRemoval> RemoveAsync(string? relativePath, string? relativeKeep, CancellationToken cancellationToken = default)
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

        var ids = await indexer.IndexFilesAsync([remove, keep], cancellationToken);
        if (!ids.TryGetValue(keep, out var keepId))
            return DuplicateRemoval.Fail(DuplicateRemovalStatus.Invalid, $"“{relativeKeep}” cannot be indexed (ignored, or its path is too long), so it cannot take the tags.", "keep");

        // Tags first: if the delete then fails, both copies have them, which loses nothing.
        await using var db = await dbFactory.CreateDbContextAsync(cancellationToken);
        if (ids.TryGetValue(remove, out var removeId))
        {
            var moved = await db.FileTags.Where(l => l.FileId == removeId).Select(l => l.TagId).ToListAsync(cancellationToken);
            var existing = await db.FileTags.Where(l => l.FileId == keepId).Select(l => l.TagId).ToListAsync(cancellationToken);
            foreach (var tagId in moved.Except(existing))
                db.FileTags.Add(new FileTag { FileId = keepId, TagId = tagId });
            await db.SaveChangesAsync(cancellationToken);
            db.ChangeTracker.Clear();
        }

        try
        {
            File.Delete(remove);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            tags.InvalidateFileCounts();
            return DuplicateRemoval.Fail(DuplicateRemovalStatus.Failed, $"“{relativePath}” could not be deleted: {ex.Message}");
        }

        // Its row goes without being kept as a missing file: its tags are on the copy kept.
        if (ids.ContainsKey(remove))
            await db.Files.Where(f => f.Id == removeId).ExecuteDeleteAsync(cancellationToken);
        tags.InvalidateFileCounts();
        DeleteCache(remove);
        logger.LogInformation("Deleted duplicate {Removed}, kept {Kept}", remove, keep);

        var kept = await tags.GetFileTagsAsync(paths.ToRelative(keep), cancellationToken);
        return new DuplicateRemoval(DuplicateRemovalStatus.Removed, Kept: kept);
    }

    private void DeleteCache(string fullPath)
    {
        try
        {
            File.Delete(thumbnails.GetCachePath(fullPath));
            var folder = trickplay.GetCacheFolder(fullPath);
            if (Directory.Exists(folder))
                Directory.Delete(folder, recursive: true);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            logger.LogDebug(ex, "Could not delete the cached previews of {File}", fullPath);
        }
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
public sealed record DuplicateRemoval(DuplicateRemovalStatus Status, string? Error = null, string? Field = null, FileTags? Kept = null)
{
    public static DuplicateRemoval Fail(DuplicateRemovalStatus status, string error, string? field = null) => new(status, error, field);
}
