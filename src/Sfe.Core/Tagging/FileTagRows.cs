using Microsoft.EntityFrameworkCore;
using Sfe.Core.Data;

namespace Sfe.Core.Tagging;

/// <summary>Writes file-tag links by batches, skipping those that exist.</summary>
internal static class FileTagRows
{
    /// <summary>Files read and changed per round trip.</summary>
    public const int BatchSize = 1000;

    /// <summary>Gives <paramref name="tagIds"/> to the files <paramref name="fileIds"/> (one batch) that lack them.</summary>
    /// <returns>Links added.</returns>
    public static async Task<int> AddAsync(SfeDbContext db, IReadOnlyCollection<int> fileIds, IReadOnlyCollection<int> tagIds, CancellationToken cancellationToken)
    {
        if (fileIds.Count == 0 || tagIds.Count == 0)
            return 0;
        var existing = (await db.FileTags
                .Where(l => fileIds.Contains(l.FileId) && tagIds.Contains(l.TagId))
                .Select(l => new { l.FileId, l.TagId })
                .ToListAsync(cancellationToken))
            .Select(l => (l.FileId, l.TagId))
            .ToHashSet();
        var added = 0;
        foreach (var fileId in fileIds)
        {
            foreach (var tagId in tagIds.Where(t => !existing.Contains((fileId, t))))
            {
                db.FileTags.Add(new FileTag { FileId = fileId, TagId = tagId });
                added++;
            }
        }

        await db.SaveChangesAsync(cancellationToken);
        db.ChangeTracker.Clear();
        return added;
    }

    /// <summary>
    /// Gives <paramref name="tagIds"/> to every file of <paramref name="files"/> lacking them, by batches in order of
    /// their index id (each read after the previous one is written).
    /// </summary>
    /// <returns>Links added.</returns>
    public static async Task<int> AddToAllAsync(SfeDbContext db, IQueryable<FileEntry> files, IReadOnlyCollection<int> tagIds, CancellationToken cancellationToken)
    {
        var added = 0;
        var lastId = 0;
        while (tagIds.Count > 0)
        {
            var after = lastId;
            var ids = await files
                .Where(f => f.Id > after)
                .OrderBy(f => f.Id)
                .Select(f => f.Id)
                .Take(BatchSize)
                .ToListAsync(cancellationToken);
            if (ids.Count == 0)
                break;
            lastId = ids[^1];
            added += await AddAsync(db, ids, tagIds, cancellationToken);
        }
        return added;
    }
}
