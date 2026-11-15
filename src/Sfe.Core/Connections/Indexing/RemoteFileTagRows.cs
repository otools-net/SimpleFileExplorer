using Microsoft.EntityFrameworkCore;
using Otools.Net.Sfe.Core.Data;

namespace Otools.Net.Sfe.Core.Connections.Indexing;

/// <summary>
/// Writes the library's tags on connection files (<see cref="RemoteFileTag"/>) by batches, as
/// <c>Tagging.FileTagRows</c> does for the library's files.
/// </summary>
internal static class RemoteFileTagRows
{
    /// <summary>Rows read and changed per round trip.</summary>
    public const int BatchSize = 1000;

    /// <summary>Gives <paramref name="tagIds"/> to the files <paramref name="fileIds"/> lacking them.</summary>
    /// <returns>Links added.</returns>
    public static async Task<int> AddAsync(
        SfeDbContext db,
        IReadOnlyCollection<long> fileIds,
        IReadOnlyCollection<int> tagIds,
        CancellationToken cancellationToken
    )
    {
        if (fileIds.Count == 0 || tagIds.Count == 0)
            return 0;
        var existing = (
            await db
                .RemoteFileTags.Where(l => fileIds.Contains(l.FileId) && tagIds.Contains(l.TagId))
                .Select(l => new { l.FileId, l.TagId })
                .ToListAsync(cancellationToken)
        )
            .Select(l => (l.FileId, l.TagId))
            .ToHashSet();
        var added = 0;
        foreach (var fileId in fileIds)
        {
            foreach (var tagId in tagIds.Where(t => !existing.Contains((fileId, t))))
            {
                db.RemoteFileTags.Add(new RemoteFileTag { FileId = fileId, TagId = tagId });
                added++;
            }
        }
        await db.SaveChangesAsync(cancellationToken);
        db.ChangeTracker.Clear();
        return added;
    }

    /// <summary>Takes <paramref name="tagIds"/> off the files <paramref name="fileIds"/> (one batch).</summary>
    /// <returns>Links removed.</returns>
    public static Task<int> RemoveAsync(
        SfeDbContext db,
        IReadOnlyCollection<long> fileIds,
        IReadOnlyCollection<int> tagIds,
        CancellationToken cancellationToken
    ) =>
        fileIds.Count == 0 || tagIds.Count == 0
            ? Task.FromResult(0)
            : db
                .RemoteFileTags.Where(l => fileIds.Contains(l.FileId) && tagIds.Contains(l.TagId))
                .ExecuteDeleteAsync(cancellationToken);

    /// <summary>
    /// Takes a deleted tag off every connection file, <see cref="BatchSize"/> links per statement (a tag can be on
    /// millions of them: no statement holds them all).
    /// </summary>
    /// <returns>Links removed.</returns>
    public static async Task<int> DeleteTagAsync(SfeDbContext db, int tagId, CancellationToken cancellationToken)
    {
        var removed = 0;
        while (true)
        {
            var ids = await db
                .RemoteFileTags.Where(l => l.TagId == tagId)
                .OrderBy(l => l.FileId)
                .Select(l => l.FileId)
                .Take(BatchSize)
                .ToListAsync(cancellationToken);
            if (ids.Count == 0)
                return removed;
            removed += await db
                .RemoteFileTags.Where(l => l.TagId == tagId && ids.Contains(l.FileId))
                .ExecuteDeleteAsync(cancellationToken);
        }
    }
}
